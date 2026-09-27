using MediatR;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.Teams.Commands;

/// <summary>
/// Command to terminate a user's membership in a team.
/// Termination is achieved by setting the <c>LeftAt</c> timestamp on the membership record,
/// which effectively revokes the user's team-level access.
/// </summary>
/// <param name="TeamId">The unique identifier of the team.</param>
/// <param name="UserEmail">The email of the user whose membership is being terminated.</param>
/// <param name="RoleInTeam">The specific role to be terminated (to handle users with multiple roles).</param>
/// <param name="LeftAt">
/// The timestamp when the membership ends. If null, the current UTC time is used.
/// This value marks the end of the membership and the removal of associated access rights.
/// </param>
public record TerminateMembershipCommand(
    Guid TeamId,
    string UserEmail,
    TeamRole RoleInTeam,
    DateTime? LeftAt = null) : IRequest<bool>;