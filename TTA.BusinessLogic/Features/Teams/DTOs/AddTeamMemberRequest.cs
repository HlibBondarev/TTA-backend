using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.Teams.DTOs;

/// <summary>
/// Data transfer object for adding a member to a team.
/// </summary>
/// <param name="UserEmail">The user email.</param>
/// <param name="RoleInTeam">The role assigned to the user within this team.</param>
/// <param name="IsPrimary">Indicates if this team is the user's primary team.</param>
public record AddTeamMemberRequest(
    string UserEmail,
    TeamRole RoleInTeam,
    bool IsPrimary);