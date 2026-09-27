namespace TTA.BusinessLogic.Features.Teams.DTOs;

/// <summary>
/// Response DTO containing team membership details combined with user profile information.
/// </summary>
/// <param name="MembershipId">The unique identifier of the membership record.</param>
/// <param name="UserId">The unique identifier of the user (from identity provider).</param>
/// <param name="DisplayName">The user's display name for UI purposes.</param>
/// <param name="Email">The user's registered email address.</param>
/// <param name="RoleInTeam">The integer representation of the user's role in the team.</param>
/// <param name="JoinedAt">The timestamp when the user joined the team.</param>
/// <param name="IsPrimary">Indicates if this is the user's main team for the sport.</param>
public record TeamMemberResponse(
    Guid MembershipId,
    string UserId,
    string DisplayName,
    string Email,
    int RoleInTeam,
    DateTime JoinedAt,
    bool IsPrimary);