namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Represents a strong-typed projection for player detailed report events retrieved from the database.
/// </summary>
/// <param name="MatchLineupId">The unique identifier of the match lineup entry.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The player's jersey number.</param>
/// <param name="EventId">The unique identifier of the game event, if present.</param>
/// <param name="EventName">The display name of the event definition, if present.</param>
/// <param name="IsPositive">Indicates whether the event is positive or negative, if present.</param>
/// <param name="PeriodNumber">The match period number, if present.</param>
/// <param name="EventTimestamp">The absolute UTC timestamp of the event, if present.</param>
/// <param name="NormalizedMatchTime">The relative normalized time within the match, if present.</param>
/// <param name="IsLeadToGoal">Flag indicating if the event led to a goal, if present.</param>
public record PlayerDetailedReportProjection(
    Guid MatchLineupId,
    string FirstName,
    string LastName,
    int Number,
    Guid? EventId,
    string? EventName,
    bool? IsPositive,
    int? PeriodNumber,
    DateTime? EventTimestamp,
    TimeSpan? NormalizedMatchTime,
    bool? IsLeadToGoal
);