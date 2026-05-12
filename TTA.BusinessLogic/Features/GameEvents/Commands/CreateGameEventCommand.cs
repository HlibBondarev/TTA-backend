using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.GameEvents.Commands;

/// <summary>
/// Command to create a new game event record. Returns the unique identifier of the created event.
/// </summary>
/// <param name="MatchId">The unique identifier of the match (from route).</param>
/// <param name="MatchLineupId">The unique identifier of the match lineup (nullable for team events).</param>
/// <param name="EventDefinitionId">The identifier of the event type definition.</param>
/// <param name="PeriodNumber">The match period when the event occurred.</param>
/// <param name="EventTimestamp">The UTC timestamp of the event.</param>
/// <param name="IsLeadToGoal">Indicates if the event was a direct lead to a goal.</param>
public record CreateGameEventCommand(
    Guid MatchId,
    Guid MatchLineupId,
    Guid EventDefinitionId,
    int PeriodNumber,
    DateTime EventTimestamp,
    bool IsLeadToGoal) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping CreateGameEventCommand to domain models.
/// </summary>
public static class CreateGameEventCommandExtensions
{
    /// <summary>
    /// Maps the creation command to a GameEvent entity.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <returns>A new GameEvent entity initialized with command data.</returns>
    public static GameEvent ToModel(this CreateGameEventCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        MatchLineupId = cmd.MatchLineupId,
        EventDefinitionId = cmd.EventDefinitionId,
        PeriodNumber = cmd.PeriodNumber,
        EventTimestamp = cmd.EventTimestamp,
        IsLeadToGoal = cmd.IsLeadToGoal,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a collection of creation commands to a list of GameEvent entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <returns>A list of GameEvent entities.</returns>
    public static List<GameEvent> ToModel(this IEnumerable<CreateGameEventCommand> list)
        => list.MapToList(ToModel);
}