using MediatR;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Teams.Commands;

/// <summary>
/// Command to add or update a user's membership within a team.
/// </summary>
/// <param name="TeamId">The unique identifier of the team to which the user is being added.</param>
/// <param name="UserEmail">The email address of the user to be assigned to the team.</param>
/// <param name="RoleInTeam">The specific role assigned to the user within the team context (e.g., Player, Coach).</param>
/// <param name="IsPrimary">Indicates whether this team should be marked as the user's primary team.</param>
public record AddTeamMemberCommand(
    Guid TeamId,
    string UserEmail,
    TeamRole RoleInTeam,
    bool IsPrimary) : IRequest<Guid>;

/// <summary>
/// Mapping extensions for <see cref="AddTeamMemberCommand"/>.
/// </summary>
public static class AddTeamMemberCommandExtensions
{
    public static TeamMembership ToModel(this AddTeamMemberCommand cmd, string userId) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = cmd.TeamId,
        RoleInTeam = cmd.RoleInTeam,
        IsPrimary = cmd.IsPrimary,
        JoinedAt = DateTime.UtcNow,
        UserId = userId
    };
}