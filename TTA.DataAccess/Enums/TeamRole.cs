using TTA.Common.Enums;

namespace TTA.DataAccess.Enums;

/// <summary>
/// Defines roles within a sports team.
/// Explicit integer values are mapped to the public.teammemberships.roleinteam column.
/// </summary>
public enum TeamRole
{
    HeadCoach = 0,
    AssistantCoach = 1,
    ClubDirector = 2,
    TeamManager = 3,
    Analyst = 4,
    Captain = 5,  // Explicitly set to match DB ordinal
    Player = 6  // Explicitly set to match DB ordinal
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