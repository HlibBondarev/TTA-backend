using TTA.BusinessLogic.Features.MatchLineups.Commands;

namespace TTA.BusinessLogic.Features.MatchLineups.DTOs;

/// <summary>
/// Data transfer object for updating an existing entry in the match protocol.
/// </summary>
/// <param name="Number">The jersey number assigned to the player.</param>
/// <param name="IsInStartingLineup">Indicates if the player is in the starting lineup.</param>
/// <param name="PositionId">The unique identifier for the player's position.</param>
public record UpdatePlayerInMatchLineupRequest(
    int Number,
    bool IsInStartingLineup,
    Guid PositionId);

/// <summary>
/// Mapping extensions for <see cref="UpdatePlayerInMatchLineupRequest"/>.
/// </summary>
public static class UpdateMatchLineupRequestExtensions
{
    /// <summary>
    /// Converts a request DTO to an update command.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="id">The unique identifier of the existing lineup entry.</param>
    /// <returns>A configured <see cref="UpdatePlayerInMatchLineupCommand"/>.</returns>
    public static UpdatePlayerInMatchLineupCommand ToCommand(this UpdatePlayerInMatchLineupRequest request, Guid id) => new(
         Id: id,
         Number: request.Number,
         IsInStartingLineup: request.IsInStartingLineup,
         PositionId: request.PositionId);
}