namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Response data transfer object representing a player's detailed match report, including player info and event chronologies.
/// </summary>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The player's jersey number.</param>
/// <param name="Events">The chronological collection of recorded player events.</param>
public record PlayerDetailedMatchReportResponse(
    string FirstName,
    string LastName,
    int Number,
    IEnumerable<PlayerDetailedEventResponse> Events
);

/// <summary>
/// Represents an individual event entry within a player's detailed match report.
/// </summary>
/// <param name="EventName">The name of the event definition.</param>
/// <param name="IsPositive">Indicates whether the event is positive or negative.</param>
/// <param name="PeriodNumber">The period number during which the event occurred.</param>
/// <param name="EventTimestamp">The absolute UTC timestamp of the event.</param>
/// <param name="NormalizedMatchTime">The normalized match time interval.</param>
/// <param name="IsLeadToGoal">Indicates whether the event led to a goal.</param>
public record PlayerDetailedEventResponse(
    string EventName,
    bool IsPositive,
    int PeriodNumber,
    DateTime EventTimestamp,
    TimeSpan? NormalizedMatchTime,
    bool IsLeadToGoal
);