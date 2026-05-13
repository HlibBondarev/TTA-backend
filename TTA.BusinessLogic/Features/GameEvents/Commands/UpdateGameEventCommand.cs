using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.GameEvents.Commands;

/// <summary>
/// Command to update an existing game event record.
/// </summary>
/// <param name="Id">The unique identifier of the event to update.</param>
/// <param name="MatchId">The unique identifier of the match (from route for validation).</param>
/// <param name="MatchLineupId">The updated match lineup identifier (nullable for team events).</param>
/// <param name="EventDefinitionId">The updated identifier of the event type definition.</param>
/// <param name="PeriodNumber">The updated match period.</param>
/// <param name="IsLeadToGoal">The updated goal lead status.</param>
public record UpdateGameEventCommand(
    Guid Id,
    Guid MatchId,
    Guid MatchLineupId,
    Guid EventDefinitionId,
    int PeriodNumber,
    bool IsLeadToGoal) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping UpdateGameEventCommand to domain models.
/// </summary>
public static class UpdateGameEventCommandExtensions
{
    /// <summary>
    /// Updates an existing GameEvent entity with data from the command.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <param name="model">The existing model to update.</param>
    /// <returns>The updated GameEvent entity.</returns>
    public static GameEvent SetToModel(this UpdateGameEventCommand cmd, GameEvent model)
    {
        model.MatchLineupId = cmd.MatchLineupId;
        model.EventDefinitionId = cmd.EventDefinitionId;
        model.PeriodNumber = cmd.PeriodNumber;
        model.IsLeadToGoal = cmd.IsLeadToGoal;

        return model;
    }

    /// <summary>
    /// Maps a collection of UpdateGameEventCommand to a list of updated GameEvent entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <param name="models">Collection of existing models.</param>
    /// <returns>A list of updated GameEvent entities.</returns>
    public static List<GameEvent> SetToModel(this IEnumerable<UpdateGameEventCommand> list, IEnumerable<GameEvent> models)
        => list.MapToList(cmd => cmd.SetToModel(models.First(m => m.Id == cmd.Id)));
}