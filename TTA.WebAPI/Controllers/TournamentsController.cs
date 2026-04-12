using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.BusinessLogic.Features.Tournaments.Queries;
using TTA.DataAccess.Models;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Extensions;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing tournament-related operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TournamentsController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching commands and queries.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TournamentsController(
    IMediator mediator,
    ILogger<TournamentsController> logger,
    Auth0Settings auth0Settings) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<TournamentsController> _logger = logger;
    private readonly Auth0Settings _auth0Settings = auth0Settings;

    /// <summary>
    /// Creates a new tournament.
    /// </summary>
    /// <param name="request">The tournament creation request data.</param>
    /// <param name="validator">The validator for the creation request.</param>
    /// <returns>The newly created tournament entity.</returns>
    /// <response code="200">Returns the created tournament.</response>
    /// <response code="400">If the request data is invalid or validation fails.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="404">If the tournament or associated entities were not found.</response>
    /// <response code="409">If a club with the same name already exists.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Tournament), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTournamentRequest request,
        [FromServices] IValidator<CreateTournamentRequest> validator)
    {
        _logger.LogInformation("Executing Create action for tournament: {Name}.", request.Name);

        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreateTournamentRequest: {Errors}.", validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // Get User Id from custom claim
        string userId = this.GetUserId(_auth0Settings);

        var command = request.ToCommand(userId);

        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Updates an existing tournament.
    /// </summary>
    /// <param name="id">The unique identifier of the tournament to update.</param>
    /// <param name="request">The tournament update request data.</param>
    /// <param name="validator">The validator for the update request.</param>
    /// <returns>The updated tournament entity.</returns>
    /// <response code="200">Returns the updated tournament.</response>
    /// <response code="400">If the request data is invalid or validation fails.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have permission to manage this tournament.</response>
    /// <response code="404">If the tournament or associated entities were not found.</response>
    /// <response code="409">If a club with the same name already exists.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Tournament), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateTournamentRequest request,
        [FromServices] IValidator<UpdateTournamentRequest> validator)
    {
        _logger.LogInformation("Executing Update action for tournament {Id}: {Name}.", id, request.Name);

        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for UpdateTournamentRequest {Id}: {Errors}.", id, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // Get User Id from custom claim
        string userId = this.GetUserId(_auth0Settings);

        var command = request.ToCommand(id, userId);

        var result = await _mediator.Send(command);

        return Ok(result);
        //try
        //{
        //    string userId = this.GetUserId(_auth0Settings);
        //    var command = request.ToCommand(id, userId);
        //    var result = await _mediator.Send(command);
        //    return Ok(result);
        //}
        //catch (TTA.Common.Exceptions.ForbiddenException)
        //{
        //    return Forbid(); // This returns 403
        //}
        //catch (TTA.Common.Exceptions.NotFoundException)
        //{
        //    return NotFound(); // This returns 404
        //}
    }

    /// <summary>
    /// Retrieves a specific tournament by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the tournament.</param>
    /// <returns>The tournament details.</returns>
    /// <response code="200">Returns the requested tournament.</response>
    /// <response code="404">If the tournament was not found.</response>
    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Tournament), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id)
    {
        _logger.LogInformation("Executing GetById action for Tournament {Id}.", id);

        var query = new GetTournamentByIdQuery(id);
        var result = await _mediator.Send(query);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}