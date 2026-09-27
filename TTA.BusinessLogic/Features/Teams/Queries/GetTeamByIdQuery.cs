using MediatR;
using TTA.BusinessLogic.Features.Teams.DTOs;

namespace TTA.BusinessLogic.Features.Teams.Queries;

/// <summary>
/// Query for retrieving a team's details by its unique identifier.
/// </summary>
/// <param name="TeamId">The unique identifier of the target team.</param>
public record GetTeamByIdQuery(Guid TeamId) : IRequest<TeamResponse>;