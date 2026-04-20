using MediatR;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Tournaments.Commands;

/// <summary>
/// Command to create a new tournament.
/// </summary>
/// <returns>
/// A <see cref="TournamentResponse"/> containing the details of the newly created tournament.
/// </returns>
/// <param name="SportId">The unique identifier of the sport associated with this tournament.</param>
/// <param name="ConfigurationId">The specific ruleset/configuration ID for this tournament.</param>
/// <param name="CityId">The unique identifier of the city where the tournament is hosted.</param>
/// <param name="OwnerId">The user ID of the tournament owner. This should not be updated by the client and will be set from the authenticated user context.</param>"
/// <param name="Name">The display name of the tournament.</param>
/// <param name="StartDate">The scheduled start date and time.</param>
/// <param name="EndDate">The scheduled end date and time. Must be greater than or equal to StartDate.</param>
public record CreateTournamentCommand(
    Guid SportId,
    Guid ConfigurationId,
    Guid CityId,
    string OwnerId,
    string Name,
    DateTime StartDate,
    DateTime? EndDate) : IRequest<TournamentResponse>;

/// <summary>
/// Mapping extensions for <see cref="CreateTournamentCommand"/>.
/// </summary>
public static class CreateUpdateTournamentCommandExtensions
{
    /// <summary>
    /// Maps the command to a <see cref="Tournament"/> domain model.
    /// </summary>
    public static Tournament ToModel(this CreateTournamentCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        SportId = cmd.SportId,
        ConfigurationId = cmd.ConfigurationId,
        CityId = cmd.CityId,
        OwnerId = cmd.OwnerId,
        Name = cmd.Name,
        StartDate = cmd.StartDate,
        EndDate = cmd.EndDate,
        CreatedAt = DateTime.UtcNow
    };
}