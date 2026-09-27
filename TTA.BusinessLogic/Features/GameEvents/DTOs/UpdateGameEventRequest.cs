using TTA.BusinessLogic.Features.GameEvents.Commands;

namespace TTA.BusinessLogic.Features.GameEvents.DTOs;

/// <summary>
/// Data transfer object for updating an existing game event.
/// </summary>
/// <param name="MatchLineupId">The updated match lineup identifier (nullable for team events).</param>
/// <param name="EventDefinitionId">The updated event definition identifier.</param>
/// <param name="PeriodNumber">The updated period number.</param>
/// <param name="IsLeadToGoal">The updated goal lead status.</param>
public record UpdateGameEventRequest(
    Guid MatchLineupId,
    Guid EventDefinitionId,
    int PeriodNumber,
    bool IsLeadToGoal);

/// <summary>
/// Mapping extensions for <see cref="UpdateGameEventRequest"/>.
/// </summary>
public static class UpdateGameEventRequestExtensions
{
    /// <summary>
    /// Converts a request DTO to an update command including the match context for validation.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="id">The unique identifier of the event to update.</param>
    /// <param name="matchId">The unique identifier of the match from the route.</param>
    /// <returns>A configured <see cref="UpdateGameEventCommand"/>.</returns>
    public static UpdateGameEventCommand ToCommand(this UpdateGameEventRequest request, Guid id, Guid matchId) => new(
        Id: id,
        MatchId: matchId,
        MatchLineupId: request.MatchLineupId,
        EventDefinitionId: request.EventDefinitionId,
        PeriodNumber: request.PeriodNumber,
        IsLeadToGoal: request.IsLeadToGoal);
}