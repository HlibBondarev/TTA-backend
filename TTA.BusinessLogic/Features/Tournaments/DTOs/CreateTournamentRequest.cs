using TTA.BusinessLogic.Features.Tournaments.Commands;

namespace TTA.BusinessLogic.Features.Tournaments.DTOs;

/// <summary>
/// Data transfer object for creating a new tournament.
/// </summary>
/// <param name="SportId">The unique identifier of the sport associated with this tournament.</param>
/// <param name="ConfigurationId">The specific configuration or ruleset identifier.</param>
/// <param name="CityId">The unique identifier of the city where the tournament takes place.</param>
/// <param name="Name">The display name of the tournament.</param>
/// <param name="StartDate">The scheduled start date and time.</param>
/// <param name="EndDate">The optional end date and time.</param>
public record CreateTournamentRequest(
    Guid SportId,
    Guid ConfigurationId,
    Guid CityId,
    string Name,
    DateTime StartDate,
    DateTime? EndDate);

/// <summary>
/// Mapping extensions for <see cref="CreateTournamentRequest"/>.
/// </summary>
public static class CreateTournamentRequestExtensions
{
    public static CreateTournamentCommand ToCommand(this CreateTournamentRequest request, string userId) => new(
         SportId: request.SportId,
         ConfigurationId: request.ConfigurationId,
         CityId: request.CityId,
         Name: request.Name,
         OwnerId: userId,
         StartDate: request.StartDate,
         EndDate: request.EndDate);
}