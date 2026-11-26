using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Queries;

/// <summary>
/// Query to retrieve the match analysis snapshot for a match and a tracked team.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="TeamId">The unique identifier of the tracked team.</param>
public record GetMatchAnalysisSnapshotQuery(Guid MatchId, Guid TeamId) : IRequest<MatchAnalysisSnapshotResponse>;
