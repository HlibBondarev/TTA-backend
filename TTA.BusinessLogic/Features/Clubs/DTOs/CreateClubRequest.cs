namespace TTA.BusinessLogic.Features.Clubs.DTOs;

/// <summary>
/// Data transfer object for creating a new club.
/// </summary>
/// <param name="Name">The name of the club (e.g., "Dynamo").</param>
/// <param name="CityId">The unique identifier of the city where the club is located.</param>
public record CreateClubRequest(string Name, Guid CityId);