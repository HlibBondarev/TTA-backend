namespace TTA.BusinessLogic.Features.Rosters.DTOs;

/// <summary>
/// Response data transfer object representing a player within a team's tournament roster.
/// </summary>
/// <param name="Id">The unique identifier of the roster entry.</param>
/// <param name="PlayerId">The unique identifier of the player.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="PositionId">The identifier of the assigned position definition.</param>
/// <param name="PositionName">The human-readable name of the position (e.g., Forward, Goalkeeper).</param>
/// <param name="Number">The jersey number assigned to the player for this tournament.</param>
public record RosterPlayerResponse(
    Guid Id,
    Guid PlayerId,
    string FirstName,
    string LastName,
    Guid PositionId,
    string PositionName,
    int Number);