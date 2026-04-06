namespace TTA.BusinessLogic.Features.Teams.DTOs;

/// <summary>
/// Response DTO containing team membership details combined with user profile information.
/// </summary>
public record TeamMemberResponse(
    Guid MembershipId,
    string UserId,
    string DisplayName,
    string Email,
    int RoleInTeam,
    DateTime JoinedAt,
    bool IsPrimary);