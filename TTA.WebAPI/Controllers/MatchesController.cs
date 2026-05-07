using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
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
    /// Retrieves the full lineup (protocol) for a specific match.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <returns>The full lineup for the specified match.</returns>
    /// <response code="200">Returns the lineup for the match.</response>
    /// <response code="404">If the match was not found.</response> 
    [AllowAnonymous]
    [HttpGet("{matchId:guid}/lineups")]
    [ProducesResponseType(typeof(IEnumerable<MatchLineupResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatchLineup([FromRoute] Guid matchId)
    {
        _logger.LogInformation("Retrieving lineup for match {MatchId}.", matchId);
        var query = new GetMatchLineupQuery(matchId);
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    /// <summary>
    /// Adds a player from the tournament roster to the match protocol.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="playerRosterId">The unique identifier of the player in the tournament roster.</param>
    /// <param name="request">The request containing details for adding the player to the lineup.</param>
    /// <param name="validator">The validator for the create match lineup request.</param>
    /// <returns>The unique identifier of the added player in the match lineup.</returns>
    /// <response code="200">Returns the ID of the added player in the match lineup.</response>
    /// <response code="400">If the request data is invalid or validation fails.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the match or player was not found.</response>
    [HttpPost("{matchId:guid}/lineups/{playerRosterId:guid}")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddPlayerToLineup(
        [FromRoute] Guid matchId,
        [FromRoute] Guid playerRosterId,
        [FromBody] AddPlayerToMatchLineupRequest request,
        [FromServices] IValidator<AddPlayerToMatchLineupRequest> validator)
    {
        _logger.LogInformation("Executing AddPlayerToLineup action for adding player {PlayerRosterId} to match {MatchId}.",
            playerRosterId, matchId);

        // 1. Validate the request DTO
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid) return BadRequest(validationResult.Errors);

        // 2. Authorization: Validate that the current user is the owner of the tournament associated with this match
        var authResult = await ValidateTournamentOwnership(matchId);
        if (authResult != null) return authResult;

        // 3. Map request to command and execute
        var command = request.ToCommand(matchId, playerRosterId);

        // Note: The handler for AddPlayerToMatchLineupCommand handles match and player existence checks
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Copies a specific selection of players from a team's tournament roster to the match protocol.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="request">The request body containing the list of selected player roster IDs.</param>
    /// <returns>The number of players successfully added to the match lineup.</returns>
    /// <response code="200">Returns the number of players added to the match lineup.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the match or team was not found.</response>
    [HttpPost("{matchId:guid}/teams/{teamId:guid}/lineup/copy")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CopyFromRoster(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromBody] CopyTeamRosterToMatchLineupRequest request)
    {
        _logger.LogInformation("Requested bulk copy of {Count} selected players for Team {TeamId} into Match {MatchId}.",
            request.PlayerRosterIds.Count(), teamId, matchId);

        // Validate tournament ownership before proceeding with the operation
        var validationResult = await ValidateTournamentOwnership(matchId);
        if (validationResult != null)
        {
            return validationResult;
        }

        // Map the request DTO to the business logic command
        var command = request.ToCommand(matchId, teamId);

        // Execute the command via Mediator
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Copies selected players to the match lineup. 
    /// This endpoint is specifically for team representatives/editors.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="request">The request body containing selected player roster IDs.</param>
    /// <returns>The number of players successfully added to the match lineup.</returns>
    /// <response code="200">Returns the count of players added.</response>
    /// <response code="401">If the request is not authenticated.</response>
    /// <response code="403">If the user is not authorized as a Team Editor for this team.</response>
    /// <response code="404">If the match or team is not found.</response>
    [Authorize(Policy = "TeamEditor")]
    [HttpPost("{matchId:guid}/teams/{teamId:guid}/lineup/copy-by-team")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CopyFromRosterByTeam(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromBody] CopyTeamRosterToMatchLineupRequest request)
    {
        _logger.LogInformation("Team Editor requested copy of {Count} players for Team {TeamId} in Match {MatchId}.",
            request.PlayerRosterIds.Count(), teamId, matchId);

        // Verify that the team is actually a participant in this specific match
        var match = await _mediator.Send(new GetMatchByIdWithDetailsQuery(matchId));
        if (match.HomeTeamId != teamId && match.GuestTeamId != teamId)
        {
            _logger.LogWarning("Access denied: Team {TeamId} is not part of Match {MatchId}.", teamId, matchId);
            return BadRequest("The specified team is not a participant in this match.");
        }

        var command = request.ToCommand(matchId, teamId);
        var result = await _mediator.Send(command);

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

        // 2. Authorization: Validate that the current user is the owner of the tournament associated with this match
        var authResult = await ValidateTournamentOwnership(id);
        if (authResult != null) return authResult;

        // 3. Map request to command and execute
        var command = request.ToCommand(id);

        // Note: The handler for RecordMatchResultCommand handles match existence check
        // and throws NotFoundException if match is missing,
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Validates if the current user is the owner of the tournament associated with the given match.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <returns>A <see cref="IActionResult"/> representing the error (NotFound or Forbid), or null if validation passes.</returns>
    private async Task<IActionResult?> ValidateTournamentOwnership(Guid matchId)
    {
        // The handler for GetMatchByIdWithDetailsQuery handles match existence check.
        // Note: The handler throws NotFoundException if match is missing,
        // which is handled by global middleware.
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
            _logger.LogWarning("User {UserId} is not authorized to modify match {MatchId}.", userId, matchId);
            return Forbid();
        }

        return null;
    }
}