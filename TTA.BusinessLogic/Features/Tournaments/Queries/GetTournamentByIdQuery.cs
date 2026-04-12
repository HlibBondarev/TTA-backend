using MediatR;
using TTA.BusinessLogic.Features.Tournaments.DTOs;

namespace TTA.BusinessLogic.Features.Tournaments.Queries;

/// <summary>
/// Query to retrieve a tournament by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the tournament to retrieve.</param>
public record GetTournamentByIdQuery(Guid Id) : IRequest<TournamentResponse?>;
