namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Represents a flat projection of a game event with joined details from definitions and lineups.
/// Used to avoid dynamic mapping and provide compile-time safety for repository queries.
/// </summary>
/// <param name="Id">The unique identifier of the game event.</param>
/// <param name="MatchLineupId">The identifier of the match lineup entry associated with this event.</param>
/// <param name="EventDefinitionId">The identifier of the type of event (e.g., Goal, Card).</param>
/// <param name="EventName">The display name of the event definition.</param>
/// <param name="IsPositive">Indicates whether the event is positive (e.g., a goal) or negative (e.g., a foul).</param>
/// <param name="PeriodNumber">The match period during which the event occurred (e.g., 1 or 2).</param>
/// <param name="EventTimestamp">The absolute UTC timestamp of when the event was recorded.</param>
/// <param name="NormalizedMatchTime">The relative time from the start of the match (e.g., 00:15:00 for the 15th minute).</param>
/// <param name="IsLeadToGoal">Flag indicating if this event directly resulted in a goal.</param>
/// <param name="PlayerName">The name of the player associated with the event, if applicable.</param>
/// <param name="PlayerNumber">The jersey number of the player, if applicable.</param>
/// <param name="TeamId">The unique identifier of the team the player belongs to.</param>
/// <param name="TeamName">The display name of the team.</param>
public record GameEventProjection(
    Guid Id,
    Guid MatchLineupId,
    Guid EventDefinitionId,
    string EventName,
    bool IsPositive,
    int PeriodNumber,
    DateTime EventTimestamp,
    TimeSpan? NormalizedMatchTime,
    bool IsLeadToGoal,
    string? PlayerName,
    int? PlayerNumber,
    Guid? TeamId,
    string? TeamName
);