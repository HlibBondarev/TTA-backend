using TTA.BusinessLogic.Features.GameEvents.Commands;

namespace TTA.BusinessLogic.Features.GameEvents.DTOs;

/// <summary>
/// Data transfer object for creating a new game event.
/// </summary>
/// <param name="MatchLineupId">The match lineup identifier (nullable for team events).</param>
/// <param name="EventDefinitionId">The event definition identifier.</param>
/// <param name="PeriodNumber">The period number when the event occurred.</param>
/// <param name="IsLeadToGoal">Indicates whether this event leads to a goal.</param>
public record CreateGameEventRequest(
    Guid MatchLineupId,
    Guid EventDefinitionId,
    int PeriodNumber,
    bool IsLeadToGoal);

/// <summary>
/// Mapping extensions for <see cref="CreateGameEventRequest"/>.
/// </summary>
public static class CreateGameEventRequestExtensions
{
    /// <summary>
    /// Converts a request DTO to a creation command including the match context.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="matchId">The unique identifier of the match from the route.</param>
    /// <returns>A configured <see cref="CreateGameEventCommand"/>.</returns>
    public static CreateGameEventCommand ToCommand(this CreateGameEventRequest request, Guid matchId) => new(
        MatchId: matchId,
        MatchLineupId: request.MatchLineupId,
        EventDefinitionId: request.EventDefinitionId,
        PeriodNumber: request.PeriodNumber,
        IsLeadToGoal: request.IsLeadToGoal);
}