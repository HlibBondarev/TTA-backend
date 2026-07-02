using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Queries;
using TTA.BusinessLogic.Services.Api;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.PlayerPresences.Handlers;

/// <summary>
/// Handles the execution pipeline for the <see cref="GetPlayersTimeInMatchQuery"/>.
/// Coordinates data extraction from repositories, triggers time normalization, and calculates final aggregated performance metrics.
/// </summary>
/// <param name="playerPresenceRepository">The specific data access repository for reading player field sessions.</param>
/// <param name="timeNormalizationService">The service used to retrieve piecewise-linear scale coefficients per period.</param>
/// <param name="logger">The application-scoped diagnostic system component used for debugging context.</param>
public class GetPlayersTimeInMatchQueryHandler(
    IPlayerPresenceRepository playerPresenceRepository,
    ITimeNormalizationService timeNormalizationService,
    ILogger<GetPlayersTimeInMatchQueryHandler> logger) : IRequestHandler<GetPlayersTimeInMatchQuery, IEnumerable<PlayerTimeInMatchResponse>>
{
    private readonly IPlayerPresenceRepository _playerPresenceRepository = playerPresenceRepository;
    private readonly ITimeNormalizationService _timeNormalizationService = timeNormalizationService;
    private readonly ILogger<GetPlayersTimeInMatchQueryHandler> _logger = logger;

    /// <summary>
    /// Processes the player analytics query by extracting dirty time projections, fetching scaling coefficients, and aggregating final times.
    /// </summary>
    /// <param name="request">The incoming query payload containing match and team identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests during processing.</param>
    /// <returns>A task representing the asynchronous operation, containing a collection of mapped performance responses.</returns>
    public async Task<IEnumerable<PlayerTimeInMatchResponse>> Handle(GetPlayersTimeInMatchQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting player time-in-match analytical pipeline for Match {MatchId} and Team {TeamId}.", request.MatchId, request.TeamId);

        // 1. Fetch strictly-typed projection rows via repository layer
        var dirtyTimeProjections = await _playerPresenceRepository.GetPlayersDirtyTimeByPeriodAsync(request.MatchId, request.TeamId, cancellationToken);

        var projectionsList = dirtyTimeProjections.ToList();
        if (projectionsList.Count == 0)
        {
            _logger.LogWarning("No player presence tracking rows found for Match {MatchId}, Team {TeamId}.", request.MatchId, request.TeamId);
            return Enumerable.Empty<PlayerTimeInMatchResponse>();
        }

        // 2. Optimization: Pre-fetch scale coefficients 'K' for each unique period in the match scope
        var uniquePeriods = projectionsList.Select(r => r.PeriodNumber).Distinct().ToList();
        var periodCoefficients = new Dictionary<int, double>();

        foreach (var periodNumber in uniquePeriods)
        {
            double coefficientK = await _timeNormalizationService.GetNormalizedTimeCoefficientAsync(request.MatchId, periodNumber);
            periodCoefficients[periodNumber] = coefficientK;
        }

        // 3. Group the projection rows by individual player (MatchLineupId)
        var groupedByPlayer = projectionsList.GroupBy(r => r.MatchLineupId);
        var responseAnalytics = new List<PlayerTimeInMatchResponse>();

        foreach (var playerGroup in groupedByPlayer)
        {
            double totalDirtySeconds = 0;
            double totalCleanSeconds = 0;

            foreach (var row in playerGroup)
            {
                totalDirtySeconds += row.DirtySeconds;

                // 4. Apply clean time calculation: CleanSeconds = DirtySeconds * K
                if (periodCoefficients.TryGetValue(row.PeriodNumber, out double coefficientK))
                {
                    totalCleanSeconds += row.DirtySeconds * coefficientK;
                }
                else
                {
                    // Fallback to raw linear time if coefficient map boundary is missed
                    totalCleanSeconds += row.DirtySeconds;
                }
            }

            // 5. Map accumulated metrics into standard .NET TimeSpan instances
            responseAnalytics.Add(new PlayerTimeInMatchResponse(
                MatchLineupId: playerGroup.Key,
                CleanTimeInMatch: TimeSpan.FromSeconds(totalCleanSeconds),
                DirtyTimeInMatch: TimeSpan.FromSeconds(totalDirtySeconds)
            ));
        }

        _logger.LogInformation("Successfully completed performance analytics execution for {Count} players in Match {MatchId}.", responseAnalytics.Count, request.MatchId);

        return responseAnalytics;
    }
}