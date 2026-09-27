using MediatR;
using TTA.BusinessLogic.Features.Rosters.DTOs;

namespace TTA.BusinessLogic.Features.Rosters.Queries;

/// <summary>
/// Query to retrieve all players assigned to a team's roster for a specific tournament.
/// </summary>
/// <param name="TournamentId">The unique identifier of the tournament.</param>
/// <param name="TeamId">The unique identifier of the team.</param>
public record GetTeamRosterQuery(Guid TournamentId, Guid TeamId) : IRequest<IEnumerable<RosterPlayerResponse>>;