using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.BusinessLogic.Features.Tournaments.Queries;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Extensions;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing specific game event operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="GameEventsController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching commands and queries.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
/// <param name="auth0Settings">The settings for Auth0 authentication.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GameEventsController(
    IMediator mediator,
    ILogger<GameEventsController> logger,
    Auth0Settings auth0Settings) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<GameEventsController> _logger = logger;
    private readonly Auth0Settings _auth0Settings = auth0Settings;

    /// <summary>
    /// Retrieves detailed information about a specific game event by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the game event.</param>
    /// <returns>A <see cref="GameEventResponse"/> containing event details.</returns>
    /// <response code="200">Returns the requested game event.</response>
    /// <response code="404">If the game event is not found.</response>
    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GameEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        _logger.LogInformation("Retrieving details for game event {Id}.", id);
        var result = await _mediator.Send(new GetGameEventByIdQuery(id));

        if (result == null) return NotFound($"Game event with ID {id} not found.");

        return Ok(result);
    }

    /// <summary>
    /// Deletes a game event from the system.
    /// Access is restricted to the tournament organizer.
    /// </summary>
    /// <param name="id">The unique identifier of the game event to delete.</param>
    /// <returns>A <see cref="NoContentResult"/> on success.</returns>
    /// <response code="204">If the deletion was successful.</response>
    /// <response code="400">If the event data is inconsistent (missing lineup entry).</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the game event or associated tournament is not found.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        _logger.LogInformation("Attempting to delete game event {Id} by tournament owner.", id);

        // 1. Fetch the event and check existence
        var gameEvent = await _mediator.Send(new GetGameEventByIdQuery(id));
        if (gameEvent == null)
        {
            _logger.LogWarning("Deletion failed: Game event {Id} not found.", id);
            return NotFound($"Game event with ID {id} was not found.");
        }

        // 2. Retrieve the lineup entry and check for data integrity
        var lineupItem = await _mediator.Send(new GetMatchLineupByIdQuery(gameEvent.MatchLineupId));
        if (lineupItem == null)
        {
            _logger.LogError("Data integrity error: Game event {Id} references non-existent lineup {LineupId}.", id, gameEvent.MatchLineupId);
            return BadRequest("The game event is invalid because its associated match lineup record is missing.");
        }

        // 3. Validate that the current user owns the tournament associated with this match
        var accessError = await ValidateTournamentOwnership(lineupItem.MatchId);
        if (accessError != null) return accessError;

        await _mediator.Send(new DeleteGameEventCommand(id));

        _logger.LogInformation("Game event {Id} successfully deleted by tournament owner.", id);
        return NoContent();
    }

    /// <summary>
    /// Validates if the current user is the owner of the tournament associated with the given match.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <returns>A <see cref="IActionResult"/> representing the error (NotFound or Forbid), or null if validation passes.</returns>
    private async Task<IActionResult?> ValidateTournamentOwnership(Guid matchId)
    {
        var match = await _mediator.Send(new GetMatchByIdWithDetailsQuery(matchId));

        var tournament = await _mediator.Send(new GetTournamentByIdQuery(match.TournamentId));
        if (tournament == null)
        {
            _logger.LogWarning("Validation failed: Associated tournament for match {MatchId} not found.", matchId);
            return NotFound("Associated tournament not found.");
        }

        string userId = this.GetUserId(_auth0Settings);
        if (tournament.OwnerId != userId)
        {
            _logger.LogWarning("User {UserId} is not authorized to modify events for match {MatchId}.", userId, matchId);
            return Forbid();
        }

        return null;
    }
}