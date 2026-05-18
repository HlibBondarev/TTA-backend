using MediatR;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;

namespace TTA.BusinessLogic.Features.TimeAnchors.Queries;

/// <summary>
/// Query to retrieve detailed information about a specific time anchor, scoped by match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match (used for scope validation).</param>
/// <param name="Id">The unique identifier of the time anchor.</param>
public record GetTimeAnchorByIdQuery(Guid MatchId, Guid Id) : IRequest<TimeAnchorResponse>;