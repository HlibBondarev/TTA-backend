using MediatR;

namespace TTA.BusinessLogic.Features.Teams.Commands;

/// <summary>
/// Command to terminate a user's membership in a team (Soft Delete).
/// </summary>
/// <param name="MembershipId">The unique identifier of the membership record.</param>
public record TerminateMembershipCommand(Guid MembershipId) : IRequest<bool>;