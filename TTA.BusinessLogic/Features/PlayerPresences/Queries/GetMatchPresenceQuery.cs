using MediatR;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;

namespace TTA.BusinessLogic.Features.PlayerPresences.Queries;

/// <summary>
/// Query to retrieve the complete chronological history of player presences (substitutions and active playing time) for a specific match.
/// </summary>
/// <param name="MatchId">The unique database reference identifier for the target match.</param>
public record GetMatchPresenceQuery(Guid MatchId) : IRequest<IEnumerable<PlayerPresenceResponse>>;