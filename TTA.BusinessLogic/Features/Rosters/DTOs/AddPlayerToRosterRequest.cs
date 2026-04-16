using TTA.BusinessLogic.Features.Rosters.Commands;

namespace TTA.BusinessLogic.Features.Rosters.DTOs;

/// <summary>
/// Data transfer object for adding a player to a tournament roster.
/// </summary>
/// <param name="TeamId">The unique identifier of the team the player is representing.</param>
/// <param name="PlayerId">The unique identifier of the player being assigned.</param>
/// <param name="PositionId">The unique identifier of the assigned player position.</param>
/// <param name="Number">The jersey number assigned to the player (e.g., 0-99).</param>
public record AddPlayerToRosterRequest(
    Guid PlayerId,
    Guid PositionId,
    int Number);

/// <summary>
/// Mapping extensions for <see cref="AddPlayerToRosterRequest"/>.
/// </summary>
public static class AddPlayerToRosterRequestExtensions
{
    public static AddPlayerToRosterCommand ToCommand(this AddPlayerToRosterRequest request, Guid tournamentId, Guid teamId) => new(
        TournamentId: tournamentId,
        TeamId: teamId,
        PlayerId: request.PlayerId,
        PositionId: request.PositionId,
        Number: request.Number);
}