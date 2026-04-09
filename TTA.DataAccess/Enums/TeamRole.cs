using TTA.Common.Enums;

namespace TTA.DataAccess.Enums;

public enum TeamRole
{
    HeadCoach,
    AssistantCoach,
    ClubDirector,
    TeamManager,
    Analyst,
    Captain,
    Player
}

/// <summary>
///  Maps internal <see cref="TeamRole"/>s to <see cref="AppRole"/>.
/// </summary>
public static class TeamRoleExtensions
{
    public static AppRole MapToAppRole(this TeamRole role) => role switch
    {
        TeamRole.HeadCoach or TeamRole.AssistantCoach or TeamRole.ClubDirector => AppRole.FullControl,
        TeamRole.TeamManager or TeamRole.Analyst => AppRole.Editor,
        TeamRole.Captain or TeamRole.Player => AppRole.Viewer,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, $"No mapping defined for {role}")
    };
}