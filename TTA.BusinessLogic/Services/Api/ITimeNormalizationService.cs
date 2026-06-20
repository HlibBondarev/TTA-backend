namespace TTA.BusinessLogic.Services.Api;

/// <summary>
/// Defines mathematical operations for piecewise-linear match time normalization.
/// Converts real-world elapsed time into standardized sport-specific clean match clock time.
/// </summary>
public interface ITimeNormalizationService
{
    /// <summary>
    /// Calculates the precise time normalization scaling coefficient (K) for a specific match period.
    /// Formula: K = SportConfiguration.PeriodDurationMinutes / TotalEffectiveRealDuration
    /// </summary>
    /// <param name="matchId">The unique identifier of the target match.</param>
    /// <param name="periodNumber">The sequence identifier number of the match period.</param>
    /// <returns>A double precision value representing the calculated period scaling coefficient.</returns>
    /// <exception cref="InvalidOperationException">Thrown when time anchors are missing or total active duration is zero.</exception>
    Task<double> GetNormalizedTimeCoefficientAsync(Guid matchId, int periodNumber);
}