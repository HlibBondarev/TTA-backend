using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.Players.DTOs;

/// <summary>
/// Data transfer object for creating a new player via the API.
/// </summary>
/// <param name="HomeClubId">The unique identifier of the club the player belongs to.</param>
/// <param name="FirstName">Player's first name.</param>
/// <param name="LastName">Player's last name.</param>
/// <param name="BirthDate">Player's date of birth.</param>
/// <param name="Gender">Player's gender (Male/Female).</param>
public record CreatePlayerRequest(
    Guid HomeClubId,
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    Gender Gender);
