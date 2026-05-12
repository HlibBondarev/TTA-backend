using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.GameEvents.Handlers;

/// <summary>
/// Handles the retrieval of a match events timeline.
/// </summary>
/// <param name="gameEventRepository">The repository for game event data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class GetMatchEventsTimelineQueryHandler(
    IGameEventRepository gameEventRepository,
    ILogger<GetMatchEventsTimelineQueryHandler> logger)
    : IRequestHandler<GetMatchEventsTimelineQuery, IEnumerable<GameEventResponse>>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly ILogger<GetMatchEventsTimelineQueryHandler> _logger = logger;

    /// <summary>
    /// Fetches raw event data from the repository and maps it to a chronological list of response DTOs.
    /// </summary>
    /// <param name="request">The query containing the target match identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing a collection of game event responses.</returns>
    public async Task<IEnumerable<GameEventResponse>> Handle(GetMatchEventsTimelineQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching events timeline for Match {MatchId}.", request.MatchId);

        var rawEvents = await _gameEventRepository.GetMatchEventsAsync(request.MatchId, cancellationToken);

        var response = rawEvents.Select(e => new GameEventResponse(
            Id: e.id,
            MatchLineupId: e.matchlineupid,
            EventDefinitionId: e.eventdefinitionid,
            EventName: e.eventname,
            IsPositive: e.ispositive,
            PeriodNumber: e.periodnumber,
            EventTimestamp: e.eventtimestamp,
            NormalizedMatchTime: e.normalizedmatchtime,
            IsLeadToGoal: e.isleadtogoal,
            PlayerName: e.playername,
            PlayerNumber: e.playernumber,
            TeamId: e.teamid,
            TeamName: e.teamname
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} events for Match {MatchId}.", response.Count, request.MatchId);
        return response;
    }
}