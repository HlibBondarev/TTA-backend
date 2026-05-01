using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.MatchLineups.Commands;

/// <summary>
/// Command to create a new match lineup entry. Returns the unique identifier of the created record.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="PlayerRosterId">The identifier of the player's registration in the tournament roster.</param>
/// <param name="Number">The jersey number assigned to the player.</param>
/// <param name="IsInStartingLineup">Indicates if the player is in the starting lineup.</param>
/// <param name="PositionId">The unique identifier for the player's position.</param>
public record AddPlayerToMatchLineupCommand(
    Guid MatchId,
    Guid PlayerRosterId,
    int Number,
    bool IsInStartingLineup,
    Guid PositionId) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping CreateMatchLineupCommand to domain models.
/// </summary>
public static class CreateMatchLineupCommandExtensions
{
    /// <summary>
    /// Maps a single CreateMatchLineupCommand to a MatchLineup entity.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <returns>A new MatchLineup entity initialized with command data.</returns>
    public static MatchLineup ToModel(this AddPlayerToMatchLineupCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = cmd.MatchId,
        PlayerRosterId = cmd.PlayerRosterId,
        Number = cmd.Number,
        IsInStartingLineup = cmd.IsInStartingLineup,
        PositionId = cmd.PositionId
    };

    /// <summary>
    /// Maps a collection of CreateMatchLineupCommand to a list of MatchLineup entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <returns>A list of MatchLineup entities.</returns>
    public static List<MatchLineup> ToModel(this IEnumerable<AddPlayerToMatchLineupCommand> list)
        => list.MapToList(ToModel);
}