using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Queries;

/// <summary>
/// Query to retrieve all matches associated with a specific tournament, including team names and tournament details.
/// </summary>
/// <param name="TournamentId">The unique identifier of the tournament whose matches are to be retrieved.</param>
public record GetTournamentMatchesQuery(Guid TournamentId) : IRequest<IEnumerable<MatchWithDetailsResponse>>;