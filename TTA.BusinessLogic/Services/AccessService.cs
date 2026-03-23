using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Services;

public class AccessService(IAccessRepository accessRepository) : IAccessService
{
    /// <summary>
    /// Validates user permissions by calling the repository and checking the role hierarchy.
    /// </summary>
    public async Task<bool> HasAccessAsync(
        string userId,
        AppRole requiredRole,
        TargetScope targetType,
        Guid? targetId = null)
    {
        // 1. Fetch the effective role from the DataAccess layer (which calls the DB function)
        var roleName = await accessRepository.GetUserRoleForScope(
            userId,
            targetType.ToString(),
            targetId);

        // 2. If no policy is found, access is denied by default
        if (string.IsNullOrEmpty(roleName))
        {
            return false;
        }

        // 3. Parse the string result from the database back into our AppRole enum
        if (!Enum.TryParse<AppRole>(roleName, out var userRole))
        {
            return false;
        }

        // 4. Check hierarchy: FullControl (0) <= Editor (1) <= Viewer (2)
        // If requiredRole is Editor (1), then both FullControl (0) and Editor (1) will pass.
        return userRole <= requiredRole;
    }
}
