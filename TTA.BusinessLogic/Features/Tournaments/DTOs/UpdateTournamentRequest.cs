using TTA.BusinessLogic.Features.Tournaments.Commands;

namespace TTA.BusinessLogic.Features.Tournaments.DTOs;

/// <summary>
/// Data transfer object for updating an existing tournament.
/// </summary>
/// <param name="SportId">The updated sport identifier.</param>
/// <param name="ConfigurationId">The updated configuration identifier.</param>
/// <param name="CityId">The updated city identifier.</param>
/// <param name="Name">The updated name of the tournament.</param>
/// <param name="StartDate">The updated start date.</param>
/// <param name="EndDate">The updated end date.</param>
public record UpdateTournamentRequest(
    Guid SportId,
    Guid ConfigurationId,
    Guid CityId,
    string Name,
    DateTime StartDate,
    DateTime? EndDate);

/// <summary>
/// Mapping extensions for <see cref="UpdateTournamentRequest"/>.
/// </summary>
public static class UpdateTournamentRequestExtensions
{
    public static UpdateTournamentCommand ToCommand(this UpdateTournamentRequest request, Guid id, string userId) => new(
         Id: id,
         SportId: request.SportId,
         ConfigurationId: request.ConfigurationId,
         CityId: request.CityId,
         Name: request.Name,
         OwnerId: userId,
         StartDate: request.StartDate,
         EndDate: request.EndDate);
}