using MediatR;
using TTA.BusinessLogic.Features.Teams.DTOs;

namespace TTA.BusinessLogic.Features.Teams.Queries;

public record GetTeamMembersQuery(Guid TeamId) : IRequest<IEnumerable<TeamMemberResponse>>;
