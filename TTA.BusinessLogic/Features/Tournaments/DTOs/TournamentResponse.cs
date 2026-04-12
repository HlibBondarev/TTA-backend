using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Tournaments.DTOs;

/// <summary>
/// Data transfer object representing a tournament for API responses.
/// </summary>
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