namespace TTA.BusinessLogic.Features.SportConfigurations.DTOs;

/// <summary>
/// Data transfer object for creating or configuring a sport configuration.
/// </summary>
/// <param name="SportId">The unique identifier of the associated sport entity.</param>
/// <param name="UsesCleanTime">Indicates whether match time calculation utilizes clean time logic.</param>
/// <param name="PeriodsCount">The standard number of periods in a match.</param>
/// <param name="PeriodDurationMinutes">The nominal duration of a single period in minutes.</param>
/// <param name="FieldSize">The descriptive summary of field dimensions (e.g., "25x20 sq.m.").</param>
/// <param name="RosterLimit">The maximum allowed number of players in a tournament team roster.</param>
/// <param name="LineupLimit">The maximum allowed number of players in a match protocol/lineup.</param>
/// <param name="ActivePlayersLimit">The maximum number of active players simultaneously on the field.</param>
/// <param name="Playground">The raw vector SVG string markup for rendering the interactive field layout.</param>
/// <param name="FieldLength">The exact physical length of the playing field in meters.</param>
/// <param name="FieldWidth">The exact physical width of the playing field in meters.</param>
public record CreateSportConfigurationRequest(
    Guid SportId,
    bool UsesCleanTime,
    int PeriodsCount,
    int PeriodDurationMinutes,
    string FieldSize,
    int RosterLimit,
    int LineupLimit,
    int ActivePlayersLimit,
    string Playground,
    decimal FieldLength,
    decimal FieldWidth);