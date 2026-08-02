using MediatR;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;

namespace TTA.BusinessLogic.Features.MatchLineups.Queries;

/// <summary>
/// Query to retrieve the lineup protocol for a specific team in a match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="TeamId">The unique identifier of the team.</param>
public record GetTeamMatchLineupQuery(Guid MatchId, Guid TeamId) : IRequest<IEnumerable<MatchLineupResponse>>;
