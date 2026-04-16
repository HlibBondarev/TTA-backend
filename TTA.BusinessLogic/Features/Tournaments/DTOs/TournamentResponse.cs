using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Tournaments.DTOs;

/// <summary>
/// Data transfer object representing a tournament for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the tournament.</param>
/// <param name="Name">The name of the tournament.</param>
/// <param name="SportId">The unique identifier of the sport type.</param>
/// <param name="ConfigurationId">The identifier for specific tournament rules/configuration.</param>
/// <param name="CityId">The unique identifier of the city where the tournament takes place.</param>
/// <param name="OwnerId">The unique identifier of the user who owns/created the tournament.</param>
/// <param name="StartDate">The scheduled start date and time of the tournament.</param>
/// <param name="EndDate">The scheduled end date and time of the tournament (optional).</param>
/// <param name="CreatedAt">The timestamp when the tournament record was created.</param>
public record TournamentResponse(
    Guid Id,
    string Name,
    Guid SportId,
    Guid ConfigurationId,
    Guid CityId,
    string OwnerId,
    DateTime StartDate,
    DateTime? EndDate,
    DateTime CreatedAt);

/// <summary>
/// Extensions to map from domain model to DTO.
/// </summary>
public static class TournamentMappingExtensions
{
    /// <summary>
    /// Converts a <see cref="Tournament"/> domain model to a <see cref="TournamentResponse"/>.
    /// </summary>
    public static TournamentResponse ToResponse(this Tournament model) =>
        new(model.Id, model.Name, model.SportId, model.ConfigurationId,
            model.CityId, model.OwnerId, model.StartDate, model.EndDate, model.CreatedAt);
}