using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Teams.Commands;

/// <summary>
/// Command to add or update a user's membership within a team.
/// </summary>
public record AddTeamMemberCommand(
    Guid TeamId,
    string UserId,
    TeamRole RoleInTeam,
    bool IsPrimary) : IRequest<Guid>;

/// <summary>
/// Mapping extensions for <see cref="AddTeamMemberCommand"/>.
/// </summary>
public static class AddTeamMemberCommandExtensions
{
    public static TeamMembership ToModel(this AddTeamMemberCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = cmd.TeamId,
        UserId = cmd.UserId,
        RoleInTeam = cmd.RoleInTeam,
        IsPrimary = cmd.IsPrimary,
        JoinedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a collection of AddTeamMemberCommand to a list of TeamMembership entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <returns>A list of TeamMembership entities.</returns>
    public static List<TeamMembership> ToModel(this IEnumerable<AddTeamMemberCommand> list)
        => list.MapToList(ToModel);
}