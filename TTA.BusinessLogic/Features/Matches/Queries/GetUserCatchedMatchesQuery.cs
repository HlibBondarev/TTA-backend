using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Queries;

/// <summary>
/// Query to retrieve all detailed matches tracked by a specific user.
/// </summary>
/// <param name="UserId">The unique identifier of the user.</param>
public record GetUserCatchedMatchesQuery(
    string UserId) : IRequest<IEnumerable<MatchWithDetailsResponse>>;