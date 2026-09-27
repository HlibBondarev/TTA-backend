namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Represents a strong-typed projection for team match summary report data retrieved from the database.
/// </summary>
/// <param name="MatchLineupId">The unique identifier of the match lineup entry.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The player's jersey number.</param>
/// <param name="Goals">Total goals scored by the player.</param>
/// <param name="PositiveGoalLeadingActions">Total positive actions that led to a goal.</param>
/// <param name="NegativeGoalLeadingActions">Total negative actions that led to a goal against.</param>
/// <param name="TotalPositiveActions">Total positive actions registered.</param>
/// <param name="TotalNegativeActions">Total negative actions registered.</param>
/// <param name="PlayPercentage">The percentage of time the player spent on the field relative to total match duration.</param>
public record TeamMatchSummaryReportProjection(
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