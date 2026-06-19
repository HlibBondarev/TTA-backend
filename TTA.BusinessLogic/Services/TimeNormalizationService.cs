using TTA.BusinessLogic.Services.Api;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Services;

/// <summary>
/// Implements the piecewise-linear timeline segment parsing logic to compute match clock scale coefficients.
/// Interacts strictly with the data access layer via <see cref="ITimeAnchorRepository"/>.
/// </summary>
public class TimeNormalizationService : ITimeNormalizationService
{
    private readonly ITimeAnchorRepository _timeAnchorRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeNormalizationService"/> class.
    /// </summary>
    /// <param name="timeAnchorRepository">The repository for retrieving match timeline anchor data.</param>
    public TimeNormalizationService(ITimeAnchorRepository timeAnchorRepository)
    {
        _timeAnchorRepository = timeAnchorRepository;
    }

    /// <inheritdoc />
    public async Task<double> GetNormalizedTimeCoefficientAsync(Guid matchId, int periodNumber)
    {
        // 1. Fetch the nominal rule configuration from the database via the repository chain
        var nominalDurationMinutes = await _timeAnchorRepository.GetMatchPeriodDurationMinutesAsync(matchId);
        if (nominalDurationMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid or missing sport configuration for Match {matchId}. Nominal period duration must be greater than zero.");
        }

        // 2. Fetch all recorded match anchors ordered chronologically
        var allAnchors = await _timeAnchorRepository.GetMatchAnchorsAsync(matchId);

        // 3. Filter and isolate anchors strictly belonging to the requested period scope
        var periodAnchors = allAnchors
            .Where(a => a.PeriodNumber == periodNumber)
            .OrderBy(a => a.Timestamp)
            .ToList();

        if (periodAnchors.Count < 2)
        {
            throw new InvalidOperationException(
                $"Cannot calculate normalization coefficient for Match {matchId}, Period {periodNumber}. Insufficient time anchors recorded.");
        }

        double totalEffectiveRealSeconds = 0;

        // 4. Piecewise-Linear Timeline Traversal
        // Iterate through chronological pairs to calculate durations of active segments.
        for (int i = 0; i < periodAnchors.Count - 1; i++)
        {
            var currentAnchor = periodAnchors[i];
            var nextAnchor = periodAnchors[i + 1];

            // A segment is active (the match clock is ticking) if it starts with PeriodStart or StoppageEnd
            if (currentAnchor.Type == TimeAnchorType.PeriodStart || currentAnchor.Type == TimeAnchorType.StoppageEnd)
            {
                var segmentDuration = (nextAnchor.Timestamp - currentAnchor.Timestamp).TotalSeconds;
                if (segmentDuration > 0)
                {
                    totalEffectiveRealSeconds += segmentDuration;
                }
            }
        }

        // 5. Convert accumulated active real seconds to minutes for scaling coefficient alignment
        double totalEffectiveRealMinutes = totalEffectiveRealSeconds / 60.0;

        // 6. Defensive Guard: Prevent division by zero if no active time was recorded
        if (totalEffectiveRealMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Total effective real duration for Match {matchId}, Period {periodNumber} evaluated to zero. Scaling coefficient cannot be computed.");
        }

        // 7. Calculate and return final coefficient: K = NominalMinutes / RealEffectiveMinutes
        return nominalDurationMinutes / totalEffectiveRealMinutes;
    }
}