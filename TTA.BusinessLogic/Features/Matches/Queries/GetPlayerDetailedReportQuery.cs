using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Queries;

/// <summary>
/// Query to retrieve a player's detailed match report for a specific lineup entry.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="MatchLineupId">The unique identifier of the match lineup entry.</param>
public record GetPlayerDetailedReportQuery(Guid MatchId, Guid MatchLineupId) : IRequest<PlayerDetailedMatchReportResponse>;