using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.MatchLineups.Commands;

/// <summary>
/// Command to update an existing match lineup entry.
/// </summary>
/// <param name="Id">The unique identifier of the existing lineup entry.</param>
/// <param name="Number">The updated jersey number.</param>
/// <param name="PositionId">The updated position identifier.</param>
public record UpdatePlayerInMatchLineupCommand(
    Guid Id,
    int Number,
    Guid PositionId) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping UpdateMatchLineupCommand to domain models.
/// </summary>
public static class UpdateMatchLineupCommandExtensions
{
    /// <summary>
    /// Updates an existing MatchLineup entity with data from the command.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <param name="model">The existing model to update.</param>
    /// <returns>The updated MatchLineup entity.</returns>
    public static MatchLineup SetToModel(this UpdatePlayerInMatchLineupCommand cmd, MatchLineup model)
    {
        model.Number = cmd.Number;
        model.PositionId = cmd.PositionId;

        return model;
    }

    /// <summary>
    /// Maps a collection of UpdateMatchLineupCommand to a list of updated MatchLineup entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <param name="models">Collection of existing models.</param>
    /// <returns>A list of updated MatchLineup entities.</returns>
    public static List<MatchLineup> SetToModel(this IEnumerable<UpdatePlayerInMatchLineupCommand> list, IEnumerable<MatchLineup> models)
        => list.MapToList(cmd => cmd.SetToModel(models.First(m => m.Id == cmd.Id)));
}