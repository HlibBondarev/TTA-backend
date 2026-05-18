using MediatR;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;

namespace TTA.BusinessLogic.Features.TimeAnchors.Queries;

/// <summary>
/// Query to retrieve all time anchors associated with a specific match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
public record GetMatchAnchorsQuery(Guid MatchId) : IRequest<IEnumerable<TimeAnchorResponse>>;