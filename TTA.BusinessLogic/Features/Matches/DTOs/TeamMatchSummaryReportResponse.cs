namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Response data transfer object representing a team match summary report player entry.
/// </summary>
/// <param name="MatchLineupId">The unique identifier of the match lineup entry.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The player's jersey number.</param>
/// <param name="Goals">Total goals scored by the player.</param>
/// <param name="PositiveGoalLeadingActions">Total positive actions leading to a goal.</param>
/// <param name="NegativeGoalLeadingActions">Total negative actions leading to a goal against.</param>
/// <param name="TotalPositiveActions">Total positive actions registered.</param>
/// <param name="TotalNegativeActions">Total negative actions registered.</param>
/// <param name="PlayPercentage">The calculated percentage of time spent on the field.</param>
public record TeamMatchSummaryReportResponse(
    Guid MatchLineupId,
    string FirstName,
    string LastName,
    int Number,
    int Goals,
    int PositiveGoalLeadingActions,
    int NegativeGoalLeadingActions,
    int TotalPositiveActions,
    int TotalNegativeActions,
    double PlayPercentage
);