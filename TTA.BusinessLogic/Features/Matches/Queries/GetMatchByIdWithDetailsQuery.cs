using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Queries;

/// <summary>
/// Query to retrieve a single match by its identifier, including full details such as team and tournament names.
/// </summary>
/// <param name="Id">The unique identifier of the match.</param>
public record GetMatchByIdWithDetailsQuery(Guid Id) : IRequest<MatchWithDetailsResponse>;