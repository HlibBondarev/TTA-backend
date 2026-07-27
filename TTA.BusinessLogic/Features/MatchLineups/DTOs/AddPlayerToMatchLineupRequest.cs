using TTA.BusinessLogic.Features.MatchLineups.Commands;

namespace TTA.BusinessLogic.Features.MatchLineups.DTOs;

/// <summary>
/// Data transfer object for adding a new player to the match protocol.
/// </summary>
/// <param name="Number">The jersey number assigned to the player.</param>
/// <param name="PositionId">The unique identifier for the player's position.</param>
public record AddPlayerToMatchLineupRequest(
    int Number,
    Guid PositionId);

/// <summary>
/// Mapping extensions for <see cref="AddPlayerToMatchLineupRequest"/>.
/// </summary>
public static class AddPlayerToMatchLineupRequestExtensions
{
    /// <summary>
    /// Converts a request DTO to a creation command.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="matchId">The identifier of the match.</param>
    /// <param name="playerRosterId">The identifier of the player's registration in the tournament roster.</param>
    /// <returns>A configured <see cref="AddPlayerToMatchLineupCommand"/>.</returns>
    public static AddPlayerToMatchLineupCommand ToCommand(this AddPlayerToMatchLineupRequest request, Guid matchId, Guid playerRosterId) => new(
         MatchId: matchId,
         PlayerRosterId: playerRosterId,
         Number: request.Number,
         PositionId: request.PositionId);
}