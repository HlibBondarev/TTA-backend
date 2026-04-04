using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.Teams.DTOs;

/// <summary>
/// Data transfer object for creating a new team within a club.
/// </summary>
/// <param name="Name">The display name of the team.</param>
/// <param name="SportId">The unique identifier of the sport.</param>
/// <param name="MinBirthYear">Optional minimum birth year for age-restricted teams.</param>
/// <param name="Gender">The gender category of the team.</param>
public record CreateTeamRequest(
    string Name,
    Guid SportId,
    int? MinBirthYear,
    Gender Gender);