using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.BusinessLogic.Features.Tournaments.Queries;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Extensions;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing specific entries in match lineups.
/// </summary>
/// <param name="mediator">The mediator instance for dispatching commands and queries.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
/// <param name="auth0Settings">The settings for Auth0 authentication.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MatchLineupsController(
    IMediator mediator,
    ILogger<MatchLineupsController> logger,
    Auth0Settings auth0Settings) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<MatchLineupsController> _logger = logger;
    private readonly Auth0Settings _auth0Settings = auth0Settings;

    /// <summary>
    /// Retrieves detailed information about a specific entry in the match lineup.
    /// </summary>
    /// <param name="id">The unique identifier of the match lineup entry.</param>
    /// <returns>The match lineup entry details.</returns>
    /// <response code="200">Returns the requested lineup entry.</response>
    /// <response code="404">If the lineup entry was not found.</response>
    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MatchLineupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id)
    {
        _logger.LogInformation("Retrieving match lineup entry {Id}.", id);
        var query = new GetMatchLineupByIdQuery(id);
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    /// <summary>
    /// Updates an existing entry in the match lineup (e.g., player's number or position).
    /// </summary>
    /// <param name="id">The unique identifier of the match lineup entry.</param>
    /// <param name="request">The updated information for the lineup entry.</param>
    /// <param name="validator">The validator for the update request.</param>
    /// <returns>The unique identifier of the updated entry.</returns>
    /// <response code="200">Returns the ID of the updated entry.</response>
    /// <response code="400">If the request data is invalid.</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the entry or associated tournament was not found.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdatePlayerInMatchLineupRequest request,
        [FromServices] IValidator<UpdatePlayerInMatchLineupRequest> validator)
    {
        _logger.LogInformation("Updating match lineup entry {Id}.", id);

        // 1. Validate the incoming request data
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for updating match lineup entry {Id}: {Errors}", id, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Validate ownership of the entry to ensure the user has permission to update it
        var authResult = await ValidateEntryOwnership(id);
        if (authResult != null) return authResult;

        // 3. Map the request to the command and send it through the mediator
        var command = request.ToCommand(id);
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Removes a player from the match lineup.
    /// </summary>
    /// <param name="id">The unique identifier of the match lineup entry to delete.</param>
    /// <returns>The number of affected records.</returns>
    /// <response code="200">Returns the number of records removed.</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the entry or associated tournament was not found.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        _logger.LogInformation("Deleting match lineup entry {Id}.", id);

        // 1. Validate ownership of the entry to ensure the user has permission to delete it
        var authResult = await ValidateEntryOwnership(id);
        if (authResult != null) return authResult;

        // 2. Create and send the delete command through the mediator
        var command = new DeletePlayerFromMatchLineupCommand(id);
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Validates if the current user is the owner of the tournament associated with the lineup entry.
    /// </summary>
    /// <param name="entryId">The unique identifier of the lineup entry.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the validation.</returns>
    private async Task<IActionResult?> ValidateEntryOwnership(Guid entryId)
    {
        // 1. Get the lineup entry to find the MatchId
        var entry = await _mediator.Send(new GetMatchLineupByIdQuery(entryId));

        // 2. Get the match to find the TournamentId
        var match = await _mediator.Send(new GetMatchByIdWithDetailsQuery(entry.MatchId));

        // 3. Get the tournament to check ownership
        var tournament = await _mediator.Send(new GetTournamentByIdQuery(match.TournamentId));
        if (tournament == null)
        {
            return NotFound("Associated tournament not found.");
        }

        // 4. Check if the current user is the owner of the tournament
        string userId = this.GetUserId(_auth0Settings);
        if (tournament.OwnerId != userId)
        {
            _logger.LogWarning("User {UserId} attempted unauthorized access to lineup entry {EntryId}.", userId, entryId);
            return Forbid();
        }

        return null;
    }
}