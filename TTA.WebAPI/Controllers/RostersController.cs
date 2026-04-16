using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Rosters.Commands;
using TTA.BusinessLogic.Features.Rosters.DTOs;
using TTA.BusinessLogic.Features.Rosters.Queries;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing player rosters within the context of specific tournaments.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RostersController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching commands.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
[Authorize]
[ApiController]
[Route("api/tournaments/{tournamentId:guid}/rosters")]
public class RostersController(
    IMediator mediator,
    ILogger<RostersController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<RostersController> _logger = logger;

    /// <summary>
    /// Registers a player to a team's roster for the specified tournament.
    /// </summary>
    /// <param name="tournamentId">The unique identifier of the tournament (from route).</param>
    ///  <param name="teamId">The team identifier.</param>
    /// <param name="request">The roster assignment details.</param>
    /// <param name="validator">The validator for the incoming request.</param>
    /// <returns>The unique identifier of the created roster record.</returns>
    /// <response code="201">Successfully assigned the player to the roster.</response>
    /// <response code="400">If the tournament has ended or the request data fails validation.</response>
    /// <response code="404">If the tournament, team, or player was not found.</response>
    /// <response code="409">If there is a conflict with jersey numbers or player registration.</response>
    [Authorize(Policy = "TeamEditor")]
    [HttpPost("{teamId:guid}")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddPlayer(
        [FromRoute] Guid tournamentId,
        [FromRoute] Guid teamId,
        [FromBody] AddPlayerToRosterRequest request,
        [FromServices] IValidator<AddPlayerToRosterRequest> validator)
    {
        _logger.LogInformation("Validating and processing roster assignment for Player {PlayerId} in Team {TeamId} for Tournament {TournamentId}.",
            request.PlayerId, teamId, tournamentId);

        // 1. Validate the request DTO using the injected validator
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for AddPlayerToRosterRequest: {Errors}.", validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Map DTO and Route param to Command
        var command = request.ToCommand(tournamentId, teamId);

        // 3. Dispatch to Handler
        var result = await _mediator.Send(command);

        _logger.LogInformation("Successfully created roster assignment {RosterId} for Tournament {TournamentId}.",
            result, tournamentId);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Retrieves the roster for a specific team in the tournament.
    /// </summary>
    /// <param name="tournamentId">The tournament identifier.</param>
    /// <param name="teamId">The team identifier.</param>
    /// <response code="200">Returns the team roster.</response>
    [AllowAnonymous]
    [HttpGet("{teamId:guid}")]
    [ProducesResponseType(typeof(IEnumerable<RosterPlayerResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamRoster(
        [FromRoute] Guid tournamentId,
        [FromRoute] Guid teamId)
    {
        _logger.LogInformation("Fetching roster for Team {TeamId} in Tournament {TournamentId}.", teamId, tournamentId);

        var query = new GetTeamRosterQuery(tournamentId, teamId);
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    /// <summary>
    /// Removes a player from the tournament roster.
    /// </summary>
    /// <param name="tournamentId">The tournament identifier.</param>
    /// <param name="teamId">The team identifier.</param>
    /// <param name="playerId">The player identifier.</param>
    /// <response code="204">Player successfully removed.</response>
    [Authorize(Policy = "TeamEditor")]
    [HttpDelete("{teamId:guid}/{playerId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemovePlayer(
        [FromRoute] Guid tournamentId,
        [FromRoute] Guid teamId,
        [FromRoute] Guid playerId)
    {
        _logger.LogInformation("Removing player {PlayerId} from team {TeamId} in tournament {TournamentId}.", playerId, teamId, tournamentId);

        await _mediator.Send(new RemovePlayerFromRosterCommand(tournamentId, teamId, playerId));

        return NoContent();
    }
}