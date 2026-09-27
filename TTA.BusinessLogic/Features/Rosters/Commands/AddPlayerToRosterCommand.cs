using MediatR;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Rosters.Commands;

/// <summary>
/// Command to assign a player to a specific team's roster within a tournament.
/// </summary>
/// <param name="TournamentId">The unique identifier of the tournament.</param>
/// <param name="TeamId">The unique identifier of the team the player is representing.</param>
/// <param name="PlayerId">The unique identifier of the player being assigned.</param>
/// <param name="PositionId">The unique identifier of the assigned player position.</param>
/// <param name="Number">The jersey number assigned to the player for this tournament.</param>
public record AddPlayerToRosterCommand(
    Guid TournamentId,
    Guid TeamId,
    Guid PlayerId,
    Guid PositionId,
    int Number) : IRequest<Guid>;

/// <summary>
/// Mapping extensions for <see cref="AddPlayerToRosterCommand"/>.
/// </summary>
public static class AddPlayerToRosterCommandExtensions
{
    /// <summary>
    /// Maps the <see cref="AddPlayerToRosterCommand"/> data to a <see cref="PlayerRoster"/> entity.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <returns>A new <see cref="PlayerRoster"/> entity initialized with command data.</returns>
    public static PlayerRoster ToModel(this AddPlayerToRosterCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = cmd.TournamentId,
        TeamId = cmd.TeamId,
        PlayerId = cmd.PlayerId,
        PositionId = cmd.PositionId,
        Number = cmd.Number,
        CreatedAt = DateTime.UtcNow
    };
}