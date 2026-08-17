using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Queries;

/// <summary>
/// Query to retrieve the team summary match report for a specific team.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="TeamId">The unique identifier of the team.</param>
public record GetTeamSummaryReportQuery(Guid MatchId, Guid TeamId) : IRequest<IEnumerable<TeamMatchSummaryReportResponse>>;