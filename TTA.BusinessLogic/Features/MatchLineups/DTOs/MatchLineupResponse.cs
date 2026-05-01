namespace TTA.BusinessLogic.Features.MatchLineups.DTOs;

/// <summary>
/// Response data transfer object representing a player entry in the match protocol.
/// </summary>
/// <param name="Id">The unique identifier of the match lineup entry.</param>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="PlayerRosterId">The identifier of the player's registration in the tournament roster.</param>
/// <param name="TeamId">The identifier of the team the player belongs to.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The jersey number assigned for this match.</param>
/// <param name="IsInStartingLineup">Indicates if the player is in the starting lineup.</param>
/// <param name="PositionId">The identifier of the assigned position definition.</param>
/// <param name="PositionName">The human-readable name of the position.</param>
public record MatchLineupResponse(
    Guid Id,
    Guid MatchId,
    Guid PlayerRosterId,
    Guid TeamId,
    string FirstName,
    string LastName,
    int Number,
    bool IsInStartingLineup,
    Guid PositionId,
    string PositionName);