namespace TTA.BusinessLogic.Features.Clubs.DTOs;

/// <summary>
/// Input DTO received by the API controller.
/// </summary>
public record CreateClubRequest(string Name, Guid CityId);