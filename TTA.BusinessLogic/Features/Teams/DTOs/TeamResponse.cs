namespace TTA.BusinessLogic.Features.Teams.DTOs;

/// <summary>
/// Represents detailed information about a team.
/// </summary>
/// <param name="Id">The unique identifier of the team.</param>
/// <param name="ClubId">The unique identifier of the owning club.</param>
/// <param name="SportId">The unique identifier of the associated sport.</param>
/// <param name="Name">The name of the team.</param>
/// <param name="MinBirthYear">The minimum birth year restriction for team members, if any.</param>
/// <param name="Gender">The gender category (0: Male, 1: Female).</param>
/// <param name="CreatedAt">The timestamp when the team was created.</param>
public record TeamResponse(
    Guid Id,
    Guid ClubId,
    Guid SportId,
    string Name,
    int? MinBirthYear,
    int Gender,
    DateTime CreatedAt);