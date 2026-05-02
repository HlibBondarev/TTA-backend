using MediatR;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;

namespace TTA.BusinessLogic.Features.MatchLineups.Queries;

/// <summary>
/// Query to retrieve the full lineup protocol for a specific match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
public record GetMatchLineupQuery(Guid MatchId) : IRequest<IEnumerable<MatchLineupResponse>>;