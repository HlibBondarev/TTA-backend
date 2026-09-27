namespace TTA.BusinessLogic.Features.SportConfigurations.DTOs;

/// <summary>
/// Represents detailed rules and parameters of a sport configuration.
/// </summary>
/// <param name="Id">The unique identifier of the sport configuration.</param>
/// <param name="SportId">The unique identifier of the associated sport.</param>
/// <param name="UsesCleanTime">Indicates whether the match timer stops during stoppages.</param>
/// <param name="PeriodsCount">The total number of periods in a match.</param>
/// <param name="PeriodDurationMinutes">The duration of each period in minutes.</param>
/// <param name="FieldSize">The field dimensions specification.</param>
/// <param name="RosterLimit">The maximum allowed players on a tournament roster.</param>
/// <param name="LineupLimit">The maximum allowed players in a match lineup.</param>
/// <param name="ActivePlayersLimit">The maximum allowed active players on the field simultaneously.</param>
public record SportConfigurationResponse(
    Guid Id,
    Guid SportId,
    bool UsesCleanTime,
    int PeriodsCount,
    int PeriodDurationMinutes,
    string? FieldSize,
    int RosterLimit,
    int LineupLimit,
    int ActivePlayersLimit);