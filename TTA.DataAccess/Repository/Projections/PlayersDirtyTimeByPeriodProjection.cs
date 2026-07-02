namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Represents a flat typed database projection containing raw linear ("dirty") play time 
/// spent by a player lineup row in a specific match period.
/// Used to avoid dynamic mapping and guarantee compile-time safety for analytical queries.
/// </summary>
/// <param name="MatchLineupId">The unique database identifier for the player's match lineup record.</param>
/// <param name="PeriodNumber">The specific match period number during which the time was recorded.</param>
/// <param name="DirtySeconds">The total linear elapsed time in seconds spent in the water.</param>
public record PlayersDirtyTimeByPeriodProjection(
    Guid MatchLineupId,
    int PeriodNumber,
    double DirtySeconds
);