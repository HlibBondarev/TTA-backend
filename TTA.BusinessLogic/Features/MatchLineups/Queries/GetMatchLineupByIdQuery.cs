using MediatR;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;

namespace TTA.BusinessLogic.Features.MatchLineups.Queries;

/// <summary>
/// Query to retrieve a single enriched match lineup entry by its unique identifier.
/// Returns detailed information including player names and position titles.
/// </summary>
/// <param name="Id">The unique identifier of the match lineup entry.</param>
public record GetMatchLineupByIdQuery(Guid Id) : IRequest<MatchLineupResponse>;