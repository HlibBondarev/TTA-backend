using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Queries;
using TTA.BusinessLogic.Features.TimeAnchors.Commands;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.Queries;
using TTA.BusinessLogic.Features.Tournaments.Queries;
using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;
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
/// <param name="accessService">Service for validating user access and permissions related to matches.</param>  
/// <param name="logger">The logger instance for diagnostic information.</param>
/// <param name="auth0Settings">The settings for Auth0 authentication.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MatchesController(
    IMediator mediator,
    IAccessService accessService,
    ILogger<MatchesController> logger,
    Auth0Settings auth0Settings) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly IAccessService _accessService = accessService;
    private readonly ILogger<MatchesController> _logger = logger;
    private readonly Auth0Settings _auth0Settings = auth0Settings;

    #region Match section

    // ==========================================================================================
    // Match section
    // ==========================================================================================

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
    /// <response code="409">If there is a conflict with the current state of the resource.</response>
    [HttpPost("{matchId:guid}/lineups/{playerRosterId:guid}")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
    /// <response code="400">If the request data is invalid or validation fails.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the match or team was not found.</response>
    [HttpPost("{matchId:guid}/teams/{teamId:guid}/lineup/copy")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

        // Verify that the team is actually a participant in this specific match
        var match = await _mediator.Send(new GetMatchByIdWithDetailsQuery(matchId));
        if (match.HomeTeamId != teamId && match.GuestTeamId != teamId)
        {
            _logger.LogWarning("Access denied: Team {TeamId} is not part of Match {MatchId}.", teamId, matchId);
            return BadRequest("The specified team is not a participant in this match.");
        }

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
    /// <response code="400">If the request data is invalid or validation fails.</response>
    /// <response code="401">If the request is not authenticated.</response>
    /// <response code="403">If the user is not authorized as a Team Editor for this team.</response>
    /// <response code="404">If the match or team is not found.</response>
    [Authorize(Policy = "TeamEditor")]
    [HttpPost("{matchId:guid}/teams/{teamId:guid}/lineup/copy-by-team")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

    #endregion

    #region Game Events section

    // ==========================================================================================
    // Game Events section
    // ==========================================================================================

    /// <summary>
    /// Retrieves the chronological timeline of all game events for a specific match.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <returns>A list of <see cref="GameEventResponse"/> representing the event timeline.</returns>
    /// <response code="200">If the event timeline was successfully retrieved.</response>
    /// <response code="404">If the match is not found.</response>
    [AllowAnonymous]
    [HttpGet("{matchId:guid}/events")]
    [ProducesResponseType(typeof(IEnumerable<GameEventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatchEvents([FromRoute] Guid matchId)
    {
        _logger.LogInformation("Retrieving event timeline for match {MatchId}.", matchId);
        // Note: The query handler handles match existence and returns an empty list or throws if necessary.
        var result = await _mediator.Send(new GetMatchEventsTimelineQuery(matchId));
        return Ok(result);
    }

    /// <summary>
    /// Records a new game event for a match in the context of a specific team.
    /// Access is restricted to team editors via policy.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="request">The request containing details of the game event to be recorded.</param>
    /// <param name="validator">The validator for the create game event request.</param>
    /// <returns>A <see cref="Guid"/> representing the newly created game event.</returns>
    /// <response code="201">If the game event was successfully created.</response>
    /// <response code="400">If the request is invalid.</response>
    /// <response code="403">If the user is not authorized to create the game event.</response>
    /// <response code="404">If the match or team is not found.</response>
    /// <response code="409">If there is a conflict with the current state of the resource.</response>
    [HttpPost("{matchId:guid}/teams/{teamId:guid}/events")]
    [Authorize(Policy = "TeamEditor")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordMatchEventByTeam(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromBody] CreateGameEventRequest request,
        [FromServices] IValidator<CreateGameEventRequest> validator)
    {
        _logger.LogInformation("Team {TeamId} is recording a new event for match {MatchId}.", teamId, matchId);

        // 1. Validate the request DTO
        var validationResult = await validator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreateGameEventRequest for match with {Id}: {Errors}.", matchId, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Verify that the team is actually a participant in this specific match
        var lineup = await _mediator.Send(new GetMatchLineupByIdQuery(request.MatchLineupId));

        if (lineup == null)
            return NotFound("The specified lineup entry was not found.");

        if (lineup.MatchId != matchId || lineup.TeamId != teamId)
            return Forbid();

        // 3. Map request to command and execute
        var command = request.ToCommand(matchId);
        var result = await _mediator.Send(command);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Records a new game event for a match as a tournament organizer.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="request">The request containing details of the game event to be recorded.</param>
    /// <param name="validator">The validator for the create game event request.</param>
    /// <returns>A <see cref="Guid"/> representing the newly created game event.</returns>
    /// <response code="201">If the game event was successfully created.</response>
    /// <response code="400">If the request is invalid.</response>
    /// <response code="403">If the user is not the owner of the tournament.</response>
    /// <response code="404">If the match is not found.</response>
    /// <response code="409">If there is a conflict with the current state of the resource.</response>
    [HttpPost("{matchId:guid}/events")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordMatchEvent(
        [FromRoute] Guid matchId,
        [FromBody] CreateGameEventRequest request,
        [FromServices] IValidator<CreateGameEventRequest> validator)
    {
        _logger.LogInformation("Recording a new event for match {MatchId} by tournament organizer.", matchId);

        // 1. Validate the request DTO
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreateGameEventRequest for match with {Id}: {Errors}.", matchId, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Authorization: Validate that the current user is the owner of the tournament associated with this match
        var accessError = await ValidateTournamentOwnership(matchId);
        if (accessError != null) return accessError;

        // 3. Verify that the lineup entry exists and belongs to the correct match (additional integrity check)
        var lineup = await _mediator.Send(new GetMatchLineupByIdQuery(request.MatchLineupId));

        if (lineup == null)
        {
            _logger.LogWarning("Record event failed: MatchLineup {LineupId} not found.", request.MatchLineupId);
            return NotFound($"The specified lineup entry {request.MatchLineupId} was not found.");
        }

        // 4. Map request to command and execute
        var command = request.ToCommand(matchId);
        var result = await _mediator.Send(command);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Updates an existing game event for a specific team.
    /// Access is restricted to team editors via policy.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="id">The unique identifier of the game event to update.</param>
    /// <param name="request">The request containing updated details of the game event.</param>
    /// <param name="validator">The validator for the update request.</param>
    /// <returns>A <see cref="GameEventResponse"/> representing the updated game event.</returns>
    /// <response code="200">If the game event was successfully updated.</response>
    /// <response code="400">If the request is invalid.</response>
    /// <response code="403">If the user is not authorized to update the game event.</response>
    /// <response code="404">If the game event is not found.</response>
    /// <response code="409">If there is a conflict with the current state of the resource.</response>
    [HttpPut("{matchId:guid}/teams/{teamId:guid}/events/{id:guid}")]
    [Authorize(Policy = "TeamEditor")]
    [ProducesResponseType(typeof(GameEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMatchEventByTeam(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromRoute] Guid id,
        [FromBody] UpdateGameEventRequest request,
        [FromServices] IValidator<UpdateGameEventRequest> validator)
    {
        _logger.LogInformation("Team {TeamId} is updating event {Id} in match {MatchId}.", teamId, id, matchId);

        // 1. Validate the request DTO
        var validationResult = await validator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for UpdateGameEventRequest for event {Id}: {Errors}.", id, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Verify that the team is actually a participant in this specific match
        var lineup = await _mediator.Send(new GetMatchLineupByIdQuery(request.MatchLineupId));

        if (lineup == null)
            return NotFound("The specified lineup entry was not found.");

        if (lineup.MatchId != matchId || lineup.TeamId != teamId)
            return Forbid();

        // 3. Map request to command and execute
        var command = request.ToCommand(id, matchId);
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Updates an existing game event as a tournament organizer.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="id">The unique identifier of the game event to update.</param>
    /// <param name="request">The request containing updated details of the game event.</param>
    /// <param name="validator">The validator for the update request.</param>
    /// <returns>A <see cref="GameEventResponse"/> representing the updated game event.</returns>
    /// <response code="200">If the game event was successfully updated.</response>
    /// <response code="400">If the request is invalid.</response>
    /// <response code="403">If the user is not authorized to update the game event.</response>
    /// <response code="404">If the game event is not found.</response>
    /// <response code="409">If there is a conflict with the current state of the resource.</response>
    [HttpPut("{matchId:guid}/events/{id:guid}")]
    [ProducesResponseType(typeof(GameEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMatchEvent(
        [FromRoute] Guid matchId,
        [FromRoute] Guid id,
        [FromBody] UpdateGameEventRequest request,
        [FromServices] IValidator<UpdateGameEventRequest> validator)
    {
        _logger.LogInformation("Updating event {Id} for match {MatchId} by tournament organizer.", id, matchId);

        // 1. Validate the request DTO
        var validationResult = await validator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for UpdateGameEventRequest for event {Id}: {Errors}.", id, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Authorization: Validate that the current user is the owner of the tournament associated with this match
        var accessError = await ValidateTournamentOwnership(matchId);
        if (accessError != null) return accessError;

        // 3. Verify that the lineup entry exists and belongs to the correct match (additional integrity check)
        var lineup = await _mediator.Send(new GetMatchLineupByIdQuery(request.MatchLineupId));

        if (lineup == null)
        {
            _logger.LogWarning("Update failed: MatchLineup {LineupId} not found.", request.MatchLineupId);
            return NotFound($"The specified lineup entry {request.MatchLineupId} was not found.");
        }

        // 4. Map request to command and execute
        var command = request.ToCommand(id, matchId);
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Deletes a game event in the context of a specific team. 
    /// Validates match and team integrity before deletion.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="id">The unique identifier of the game event to delete.</param>
    /// <returns>A <see cref="IActionResult"/> representing the result of the deletion operation.</returns>
    /// <response code="204">If the deletion was successful.</response>
    /// <response code="400">If the event data is inconsistent (missing lineup entry).</response>
    /// <response code="403">If the user is not authorized to delete the game event.</response>
    /// <response code="404">If the game event or associated lineup is not found.</response>
    [HttpDelete("{matchId:guid}/teams/{teamId:guid}/events/{id:guid}")]
    [Authorize(Policy = "TeamEditor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMatchEventByTeam(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromRoute] Guid id)
    {
        _logger.LogInformation("Team {TeamId} is attempting to delete event {Id} in match {MatchId}.", teamId, id, matchId);

        // 1. Fetch the event to verify existence and context
        var gameEvent = await _mediator.Send(new GetGameEventByIdQuery(id));
        if (gameEvent == null) return NotFound($"Game event with ID {id} not found.");

        // 2. Fetch lineup to verify match and team ownership (MatchLineupId is now Guid)
        var lineupItem = await _mediator.Send(new GetMatchLineupByIdQuery(gameEvent.MatchLineupId));

        if (lineupItem == null)
        {
            _logger.LogError("Data integrity error: Event {Id} exists but its lineup record is missing.", id);
            return BadRequest("The operation cannot be completed due to a missing lineup record.");
        }

        // 3. Security & Integrity check: Route parameters must match the database record
        if (lineupItem.MatchId != matchId || lineupItem.TeamId != teamId)
        {
            _logger.LogWarning("Unauthorized deletion attempt: Event {Id} context mismatch (Match/Team).", id);
            return Forbid();
        }

        // 4. Proceed with deletion
        await _mediator.Send(new DeleteGameEventCommand(id));

        return NoContent();
    }

    /// <summary>
    /// Triggers batch event time normalization for a match in the context of a specific team.
    /// Access is strictly restricted to team editors via security policies.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="validator">The shared fluent validator instance injected from DI.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    [HttpPut("{matchId:guid}/teams/{teamId:guid}/events/normalize")]
    [Authorize(Policy = "TeamEditor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NormalizeMatchTimeByTeam(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromServices] IValidator<NormalizeMatchTimeRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Team editor for team {TeamId} is attempting to normalize time logs for match {MatchId}.", teamId, matchId);

        // 1. Validate the incoming route parameters packed into the shared DTO
        var request = new NormalizeMatchTimeRequest(matchId, teamId);
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for NormalizeMatchTimeRequest in team normalization context for match {MatchId}.", matchId);
            return BadRequest(validationResult.Errors);
        }

        // 2. Access Control Invariant: Verify that the team actually participates in this specific match protocol.
        // GetMatchByIdWithDetailsQuery automatically triggers a NotFoundException via middleware if the match is missing.
        var match = await _mediator.Send(new GetMatchByIdWithDetailsQuery(matchId), cancellationToken);

        if (match.HomeTeamId != teamId && match.GuestTeamId != teamId)
        {
            _logger.LogWarning("Authorization breach: Team {TeamId} is not a participant in Match {MatchId}.", teamId, matchId);
            return Forbid();
        }

        // 3. Map to MediatR execution command payload
        var command = new NormalizeMatchTimeCommand(matchId, teamId);
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Triggers batch event time normalization for a team's events as a tournament organizer.
    /// Access is authorized based on global tournament ownership metrics.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the target team whose events require updates.</param>
    /// <param name="validator">The shared fluent validator instance injected from DI.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    [HttpPut("{matchId:guid}/teams/{teamId:guid}/events/normalize-admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NormalizeMatchTime(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromServices] IValidator<NormalizeMatchTimeRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Tournament organizer is executing batch match time normalization for match {MatchId} and team {TeamId}.", matchId, teamId);

        // 1. Validate the request parameters context using the same shared DTO
        var request = new NormalizeMatchTimeRequest(matchId, teamId);
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for NormalizeMatchTimeRequest in admin normalization context for match {MatchId}.", matchId);
            return BadRequest(validationResult.Errors);
        }

        // 2. Authorization validation: Enforce that the user owns the tournament associated with this match
        var accessError = await ValidateTournamentOwnership(matchId);
        if (accessError != null) return accessError;

        // 3. Dispatch the task across the mediator pipelines
        var command = new NormalizeMatchTimeCommand(matchId, teamId);
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }

    #endregion

    #region Time Anchors section

    // ==========================================================================================
    // Time Anchors section
    // ==========================================================================================

    /// <summary>
    /// Retrieves the complete chronological timeline of anchors recorded for a specific match.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of time anchor details.</returns>
    /// <response code="200">Returns the list of time anchors.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="404">If the match or associated entities were not found.</response>
    [AllowAnonymous]
    [HttpGet("{matchId}/anchors")]
    [ProducesResponseType(typeof(IEnumerable<TimeAnchorResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatchAnchors(Guid matchId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving time anchors for match {MatchId}.", matchId);
        var anchors = await _mediator.Send(new GetMatchAnchorsQuery(matchId), cancellationToken);

        return Ok(anchors);
    }

    /// <summary>
    /// Retrieves a specific time anchor record by its unique identifier.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match (from route context).</param>
    /// <param name="id">The unique identifier of the time anchor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detailed representation of the requested time anchor.</returns>
    /// <response code="200">Returns the requested time anchor.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="404">If the time anchor or associated match was not found.</response>
    [AllowAnonymous]
    [HttpGet("{matchId}/anchors/{id}")]
    [ProducesResponseType(typeof(TimeAnchorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeAnchorById(Guid matchId, Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving time anchor {Id} for match {MatchId}.", id, matchId);
        var anchor = await _mediator.Send(new GetTimeAnchorByIdQuery(matchId, id), cancellationToken);

        return Ok(anchor);
    }

    /// <summary>
    /// Records a new time anchor for a match timeline.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match from the route.</param>
    /// <param name="request">The data transfer object containing anchor specifications.</param>
    /// <param name="validator">The request validator injected from DI container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unique identifier of the newly created time anchor record.</returns>
    /// <response code="201">Returns the unique identifier of the newly created time anchor.</response>
    /// <response code="400">If the request is invalid.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user is not authorized to perform this action.</response>
    /// <response code="404">If the match or associated entities were not found.</response>
    [HttpPost("{matchId}/anchors")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordTimeAnchor(
        Guid matchId,
        [FromBody] CreateTimeAnchorRequest request,
        [FromServices] IValidator<CreateTimeAnchorRequest> validator,
        CancellationToken cancellationToken)
    {
        // 1) Execute FluentValidation rules against the incoming request model
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreateTimeAnchorRequest in match {MatchId}.", matchId);
            return BadRequest(validationResult.Errors);
        }

        // 2) Verify user authorization rules (Tournament Owner or competing Team Editors)
        var authResult = await ValidateMatchEditAccess(matchId, cancellationToken);
        if (authResult != null) return authResult;

        // 3) Map the validated request DTO to the corresponding command and execute it
        var command = request.ToCommand(matchId);
        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Permanently removes a specific time anchor record from the match timeline.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match from the route.</param>
    /// <param name="id">The unique identifier of the time anchor to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An empty response indicating successful processing.</returns>
    /// <response code="204">Indicates successful deletion of the time anchor.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user is not authorized to perform this action.</response>
    /// <response code="404">If the time anchor or associated match was not found.</response>
    [HttpDelete("{matchId}/anchors/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTimeAnchor(Guid matchId, Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to delete time anchor {Id} from match {MatchId}.", id, matchId);

        // Verify user authorization rules before performing destructive database changes
        var authResult = await ValidateMatchEditAccess(matchId, cancellationToken);
        if (authResult != null) return authResult;

        // Note: The handler for DeleteTimeAnchorCommand handles existence check and throws NotFoundException if anchor is missing,
        // pass both matchId and id to the command
        await _mediator.Send(new DeleteTimeAnchorCommand(matchId, id), cancellationToken);

        return NoContent();
    }

    #endregion

    #region Player Presences section

    // ==========================================================================================
    // Player Presences section
    // ==========================================================================================

    /// <summary>
    /// Executes a player substitution during a specific match period.
    /// Closes the session for the outgoing player and opens a session for the incoming player identically in time.
    /// </summary>
    /// <param name="matchId">The unique identity reference key of the target match extracted from the route path context.</param>
    /// <param name="request">The incoming data transfer object payload containing period numbers and lineup references data structures.</param>
    /// <param name="validator">The fluent validator service instance injected directly via method services to manage request structural integrity checks.</param>
    /// <param name="cancellationToken">A secure execution propagation token designed for watching asynchronous task cancellation requests states.</param>
    /// <returns>An HTTP action result containing the unique identifier database value of the newly recorded incoming player session asset.</returns>
    [HttpPost("{matchId}/substitutions")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubstitutePlayer(
        [FromRoute] Guid matchId,
        [FromBody] SubstitutePlayerRequest request,
        [FromServices] IValidator<SubstitutePlayerRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received request to substitute players in Match {MatchId}, Period {Period}.", matchId, request.PeriodNumber);

        // 1. Explicit FluentValidation invocation inside controller layer via injected method service parameter
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation collapse occurred for SubstitutePlayerRequest payload in Match {MatchId}.", matchId);
            return BadRequest(validationResult.Errors);
        }

        // 2. Strict match-level domain edit scope access privilege validation check
        var authResult = await ValidateMatchEditAccess(matchId, cancellationToken);
        if (authResult != null)
        {
            return authResult;
        }

        // 3. Transformation mapping conversion invocation and mediator request dispatch routing execution sequence
        var command = request.ToCommand(matchId);
        var presenceId = await _mediator.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, presenceId);
    }

    /// <summary>
    /// Bulk initializes active presence tracking timelines for an explicit array configuration of players starting a specific match period section.
    /// </summary>
    /// <param name="matchId">The unique identity reference key of the target match extracted from the route path context.</param>
    /// <param name="request">The incoming data transfer object payload containing period markers and distinct starting lineup identifier elements collections.</param>
    /// <param name="validator">The fluent validator service instance injected directly via method services to manage initialization request structural integrity checks.</param>
    /// <param name="cancellationToken">A secure execution propagation token designed for watching asynchronous task cancellation requests states.</param>
    /// <returns>An HTTP 204 No Content success state response tracking correct operational transactional completions flags.</returns>
    [HttpPost("{matchId}/presence/initialize")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InitializePeriodPresence(
        [FromRoute] Guid matchId,
        [FromBody] InitializePresenceRequest request,
        [FromServices] IValidator<InitializePresenceRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received request to initialize period presence log arrays for Match {MatchId}, Period {Period}.", matchId, request.PeriodNumber);

        // 1. Explicit FluentValidation invocation inside controller layer via injected method service parameter
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation collapse occurred for InitializePresenceRequest payload in Match {MatchId}.", matchId);
            return BadRequest(validationResult.Errors);
        }

        // 2. Strict match-level domain edit scope access privilege validation check
        var authResult = await ValidateMatchEditAccess(matchId, cancellationToken);
        if (authResult != null)
        {
            return authResult;
        }

        // 3. Transformation mapping conversion invocation and mediator request dispatch routing execution sequence
        var command = request.ToCommand(matchId);
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Retrieves the complete chronological historical tracking sequence list data structure of all player presences and substitutions log entries logged against a match.
    /// </summary>
    /// <param name="matchId">The unique identity reference key of the target match extracted from the route path context.</param>
    /// <param name="cancellationToken">A secure execution propagation token designed for watching asynchronous task cancellation requests states.</param>
    /// <returns>An HTTP action response wrapper holding a structured collection sequence stream array of player presence data objects entities.</returns>
    [AllowAnonymous]
    [HttpGet("{matchId}/presence")]
    [ProducesResponseType(typeof(IEnumerable<PlayerPresenceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatchPresence(
        [FromRoute] Guid matchId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received HTTP GET data query request to fetch player presence logs timeline history tracking details for Match {MatchId}.", matchId);

        var query = new GetMatchPresenceQuery(matchId);
        var response = await _mediator.Send(query, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Calculates the clean and dirty play time performance analytics for players of a specific team in a match.
    /// Accessible only by users holding the TeamEditor policy.
    /// </summary>
    /// <param name="matchId">The unique identifier of the targeted match.</param>
    /// <param name="teamId">The unique identifier of the team whose player analytics are being calculated.</param>
    /// <param name="validator">The FluentValidation instance injected from services to verify parameters.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests during processing.</param>
    /// <returns>An <see cref="IActionResult"/> wrapping the collection of calculated player time-in-match metrics.</returns>
    [HttpGet("{matchId:guid}/teams/{teamId:guid}/presence/calculate")]
    [Authorize(Policy = "TeamEditor")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PlayerTimeInMatchResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlayersTimeInMatchByTeam(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromServices] IValidator<PlayerTimeInMatchRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating team-level player time-in-match calculation for Match: {MatchId}, Team: {TeamId}.", matchId, teamId);

        // 1. Instantiate the request record and execute FluentValidation boundary checks
        var request = new PlayerTimeInMatchRequest(matchId, teamId);
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for team-level player analytics request in Match {MatchId}.", matchId);
            return BadRequest(validationResult.Errors);
        }

        // 2. Dispatch query to validate tenancy and boundaries
        var match = await _mediator.Send(new GetMatchByIdWithDetailsQuery(matchId), cancellationToken);
        if (match == null)
        {
            _logger.LogWarning("Tenancy validation failed: Match {MatchId} not found.", matchId);
            return NotFound($"Match with ID {matchId} was not found.");
        }

        // 3. Enforce boundary rules: teamId must belong to either Home or Guest team of the match context
        if (teamId != match.HomeTeamId && teamId != match.GuestTeamId)
        {
            _logger.LogWarning("Access denied: Team {TeamId} does not belong to Match {MatchId} context boundaries.", teamId, matchId);
            return Forbid();
        }

        // 4. Dispatch the core performance analytics query pipeline
        var analyticsResult = await _mediator.Send(new GetPlayersTimeInMatchQuery(matchId, teamId), cancellationToken);

        return Ok(analyticsResult);
    }

    /// <summary>
    /// Calculates the clean and dirty play time performance analytics for tournament administrators.
    /// Protected via centralized tournament ownership routine verification blocks.
    /// </summary>
    /// <param name="matchId">The unique identifier of the targeted match.</param>
    /// <param name="teamId">The unique identifier of the team whose player analytics are being calculated.</param>
    /// <param name="validator">The FluentValidation instance injected from services to verify parameters.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests during processing.</param>
    /// <returns>An <see cref="IActionResult"/> wrapping the collection of calculated player time-in-match metrics.</returns>
    [HttpGet("{matchId:guid}/teams/{teamId:guid}/presence/calculate-admin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PlayerTimeInMatchResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlayersTimeInMatch(
        [FromRoute] Guid matchId,
        [FromRoute] Guid teamId,
        [FromServices] IValidator<PlayerTimeInMatchRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating admin-level player time-in-match calculation for Match: {MatchId}, Team: {TeamId}.", matchId, teamId);

        // 1. Instantiate the request record and execute FluentValidation boundary checks
        var request = new PlayerTimeInMatchRequest(matchId, teamId);
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for admin-level player analytics request in Match {MatchId}.", matchId);
            return BadRequest(validationResult.Errors);
        }

        // 2. Execute administrative tournament ownership validation routine block (fixed signature)
        var validationResultBlock = await ValidateTournamentOwnership(matchId);
        if (validationResultBlock != null)
        {
            return validationResultBlock;
        }

        // 3. Dispatch the identical core performance analytics query pipeline
        var analyticsResult = await _mediator.Send(new GetPlayersTimeInMatchQuery(matchId, teamId), cancellationToken);

        return Ok(analyticsResult);
    }

    #endregion

    #region Helper Methods

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

    /// <summary>
    /// Validates if the current user has permission to modify the match data.
    /// Authorized roles: Tournament Owner OR Team Editors for either of the competing teams.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="IActionResult"/> representing the error (NotFound or Forbid), or null if validation passes.</returns>
    private async Task<IActionResult?> ValidateMatchEditAccess(Guid matchId, CancellationToken cancellationToken = default)
    {
        // Fetch match details to retrieve tournament and team identifiers
        var match = await _mediator.Send(new GetMatchByIdWithDetailsQuery(matchId), cancellationToken);

        // Verify associated tournament existence and ownership
        var tournament = await _mediator.Send(new GetTournamentByIdQuery(match.TournamentId), cancellationToken);
        if (tournament == null)
        {
            _logger.LogWarning("Validation failed: Associated tournament for match {MatchId} not found.", matchId);
            return NotFound("Associated tournament not found.");
        }

        string userId = this.GetUserId(_auth0Settings);
        if (tournament.OwnerId == userId)
        {
            // User is the tournament creator -> Access Granted immediately
            return null;
        }

        // Check Team Editor rights for the Home Team
        bool isHomeEditor = await _accessService.HasAccessAsync(
            userId,
            AppRole.Editor,
            TargetScope.Team,
            match.HomeTeamId,
            cancellationToken);

        if (isHomeEditor)
        {
            return null; // Access Granted
        }

        // Check Team Editor rights for the Guest Team (Correction: Using GuestTeamId from MatchWithDetailsResponse)
        bool isGuestEditor = await _accessService.HasAccessAsync(
            userId,
            AppRole.Editor,
            TargetScope.Team,
            match.GuestTeamId,
            cancellationToken);

        if (isGuestEditor)
        {
            return null; // Access Granted
        }

        // If none of the conditions above are met -> Access Denied
        _logger.LogWarning("User {UserId} is not authorized to modify match {MatchId}.", userId, matchId);
        return Forbid();
    }

    #endregion
}