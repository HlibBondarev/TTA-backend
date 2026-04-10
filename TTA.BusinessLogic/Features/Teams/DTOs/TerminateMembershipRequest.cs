using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.Teams.DTOs;

/// <summary>
/// Data transfer object for terminating a user's membership and access rights.
/// </summary>
/// <param name="UserEmail">The email address of the user to terminate.</param>
/// <param name="RoleInTeam">The role assigned to the user within this team.</param>
/// <param name="LeftAt">Optional termination date. If null, current time is used. Must not be in the past.</param>
public record TerminateMembershipRequest(
    string UserEmail,
    TeamRole RoleInTeam,
    DateTime? LeftAt);