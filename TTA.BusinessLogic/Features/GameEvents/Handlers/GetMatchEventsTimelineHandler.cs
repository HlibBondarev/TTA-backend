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
public class GetMatchEventsTimelineHandler(
    IGameEventRepository gameEventRepository,
    ILogger<GetMatchEventsTimelineHandler> logger)
    : IRequestHandler<GetMatchEventsTimelineQuery, IEnumerable<GameEventResponse>>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly ILogger<GetMatchEventsTimelineHandler> _logger = logger;

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

        // Sorting logic: 
        // 1. NormalizedMatchTime ASC (nulls last using TimeSpan.MaxValue as sentinel)
        // 2. EventTimestamp ASC as a tie-breaker
        var response = rawEvents
            .OrderBy(e => e.NormalizedMatchTime ?? TimeSpan.MaxValue)
            .ThenBy(e => e.EventTimestamp)
            .Select(e => new GameEventResponse(
                Id: e.Id,
                MatchLineupId: e.MatchLineupId,
                EventDefinitionId: e.EventDefinitionId,
                EventName: e.EventName,
                IsPositive: e.IsPositive,
                PeriodNumber: e.PeriodNumber,
                EventTimestamp: e.EventTimestamp,
                NormalizedMatchTime: e.NormalizedMatchTime,
                IsLeadToGoal: e.IsLeadToGoal,
                PlayerName: e.PlayerName,
                PlayerNumber: e.PlayerNumber,
                TeamId: e.TeamId,
                TeamName: e.TeamName
            )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} events for Match {MatchId}.", response.Count, request.MatchId);

        return response;
    }
}