namespace TTA.BusinessLogic.Features.GameEvents.DTOs;

/// <summary>
/// Response data transfer object representing a game event, 
/// mapped exactly from the 'public.get_match_events' storage function output.
/// </summary>
/// <param name="Id">The unique identifier of the game event record.</param>
/// <param name="MatchLineupId">The identifier of the match lineup entry (nullable).</param>
/// <param name="EventDefinitionId">The identifier of the specific event type definition.</param>
/// <param name="EventName">The human-readable name of the event type.</param>
/// <param name="IsPositive">Indicates if the event has a positive impact based on definition.</param>
/// <param name="PeriodNumber">The match period when the event occurred.</param>
/// <param name="EventTimestamp">The UTC timestamp of the event.</param>
/// <param name="NormalizedMatchTime">The calculated match time (interval) when the event occurred.</param>
/// <param name="IsLeadToGoal">Indicates if the event was a direct lead to a goal.</param>
/// <param name="PlayerName">The full name of the player (combined first and last name).</param>
/// <param name="PlayerNumber">The jersey number of the player from the match lineup.</param>
/// <param name="TeamId">The unique identifier of the team associated with the event.</param>
/// <param name="TeamName">The name of the team associated with the event.</param>
public record GameEventResponse(
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
    string? TeamName);