using Microsoft.AspNetCore.Authorization;
using TTA.Common.Enums;

namespace TTA.WebAPI.Authorization;

/// <summary>
/// Represents an authorization requirement for scoped permissions.
/// Defines the minimum role level and the target resource type (Global, Club, or Team).
/// </summary>
/// <param name="requiredRole">The minimum role level required to fulfill the requirement.</param>
/// <param name="targetType">The specific scope (Club, Team, or Global) to which the requirement applies.</param>
public class ScopePermissionRequirement(AppRole requiredRole, TargetScope targetType) : IAuthorizationRequirement
{
    /// <summary>
    /// Gets the minimum role required (e.g., Viewer, Editor, FullControl).
    /// </summary>
    public AppRole RequiredRole { get; } = requiredRole;

    /// <summary>
    /// Gets the target scope of the permission.
    /// </summary>
    public TargetScope TargetType { get; } = targetType;
}