using MediatR;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Teams.Commands;

/// <summary>
/// Command to add or update a user's membership within a team.
/// </summary>
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