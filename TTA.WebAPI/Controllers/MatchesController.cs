using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.BusinessLogic.Features.Tournaments.Queries;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Extensions;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing specific match operations and results.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MatchesController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching commands and queries.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
/// <param name="auth0Settings">The settings for Auth0 authentication.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MatchesController(
    IMediator mediator,
    ILogger<MatchesController> logger,
    Auth0Settings auth0Settings) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<MatchesController> _logger = logger;
    private readonly Auth0Settings _auth0Settings = auth0Settings;

    /// <summary>
    /// Retrieves detailed information about a specific match.
    /// </summary>
    /// <param name="id">The unique identifier of the match.</param>
    /// <returns>Detailed match information including team names.</returns>
    /// <response code="200">Returns the requested match details.</response>
    /// <response code="404">If the match was not found.</response>
    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MatchWithDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id)
    {
        _logger.LogInformation("Executing GetById action for match {Id}.", id);

        var query = new GetMatchByIdWithDetailsQuery(id);
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    /// <summary>
    /// Records the score and conditions for an existing match.
    /// Only the tournament organizer has permission to record results.
    /// </summary>
    /// <param name="id">The unique identifier of the match.</param>
    /// <param name="request">The request containing scores and weather conditions.</param>
    /// <param name="validator">The validator for the record result request.</param>
    /// <returns>The unique identifier of the updated match.</returns>
    /// <response code="200">Returns the ID of the updated match.</response>
    /// <response code="400">If the request data is invalid or validation fails.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the match or associated tournament was not found.</response>
    [HttpPut("{id:guid}/result")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordResult(
        [FromRoute] Guid id,
        [FromBody] RecordMatchResultRequest request,
        [FromServices] IValidator<RecordMatchResultRequest> validator)
    {
        _logger.LogInformation("Executing RecordResult action for match {Id}.", id);

        // 1. Validate the request DTO
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for RecordMatchResultRequest {Id}: {Errors}.", id, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Fetch match details to identify the parent tournament
        var matchQuery = new GetMatchByIdWithDetailsQuery(id);
        var match = await _mediator.Send(matchQuery);

        // Note: The handler for GetMatchByIdWithDetailsQuery should throw NotFoundException if match is missing,
        // which is handled by global middleware.

        // 3. Authorization Check: Fetch tournament to verify ownership
        string userId = this.GetUserId(_auth0Settings);
        var tournamentQuery = new GetTournamentByIdQuery(match.TournamentId);
        var tournament = await _mediator.Send(tournamentQuery);

        if (tournament == null)
        {
            return NotFound("Associated tournament not found.");
        }

        if (tournament.OwnerId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to record results for match {Id} in tournament {TournamentId} without permission.",
                userId, id, match.TournamentId);
            return Forbid();
        }

        // 4. Map request to command and execute
        var command = request.ToCommand(id);
        var result = await _mediator.Send(command);

        return Ok(result);
    }
}