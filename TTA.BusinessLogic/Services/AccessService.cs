using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Services;

public class AccessService(IAccessRepository accessRepository) : IAccessService
{
    /// <summary>
    /// Validates user permissions by calling the repository and checking the role hierarchy.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="requiredRole">The minimum role level required for access.</param>
    /// <param name="targetType">The scope level (Club, Team, or Global).</param>
    /// <param name="targetId">The specific ID of the resource (optional).</param>
    /// <returns>True if the user's role is sufficient; otherwise, false.</returns>
    public async Task<bool> HasAccessAsync(
        string userId,
        AppRole requiredRole,
        TargetScope targetType,
        Guid? targetId = null)
    {
        // 1. Fetch the effective role from the DataAccess layer. 
        // The repository now returns AppRole? directly, so no parsing needed.
        var userRole = await accessRepository.GetUserRoleForScope(
            userId,
            targetType,
            targetId);

        // 2. If no role is found in the database, access is denied by default.
        if (userRole == null)
        {
            return false;
        }

        // 3. Check hierarchy: FullControl (0) <= Editor (1) <= Viewer (2).
        // Since it's an enum, we can compare directly.
        return userRole <= requiredRole;
    }
}