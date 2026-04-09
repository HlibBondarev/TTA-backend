using MediatR;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.Teams.Commands;

/// <summary>
/// Command to terminate a user's membership and associated access policies within a specific team.
/// This operation performs a soft delete by setting termination and expiration timestamps.
/// </summary>
/// <param name="TeamId">The unique identifier of the team from which the user is being removed.</param>
/// <param name="UserEmail">The email address of the user whose membership is to be terminated.</param>
/// <param name="RoleInTeam">The specific role of the user within the team that is being revoked.</param>
/// <param name="LeftAt">
/// Optional termination date and time. 
/// If provided, this value is used for both the membership 'LeftAt' field and the access policy 'ExpiresAt' field.
/// If <c>null</c>, the current system UTC time will be used.
/// </param>
public record TerminateMembershipCommand(
    Guid TeamId,
    string UserEmail,
    TeamRole RoleInTeam,
    DateTime? LeftAt) : IRequest<bool>;