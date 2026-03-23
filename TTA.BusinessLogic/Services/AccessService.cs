using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Services;

/// <inheritdoc cref="IAccessService" />
public class AccessService(IAccessRepository accessRepository) : IAccessService
{
    /// <inheritdoc />
    public async Task<bool> HasAccessAsync(
        string userId,
        AppRole requiredRole,
        TargetScope targetScope,
        Guid? targetId = null,
        CancellationToken ct = default)
    {
        // 1. Fetch the effective role from the DataAccess layer.
        // The repository now returns AppRole? directly, so we don't need string parsing.
        var userRole = await accessRepository.GetUserRoleForScope(
            userId,
            targetScope,
            targetId,
            ct);

        // 2. If no role is found in the database, access is denied by default.
        if (userRole == null)
        {
            return false;
        }

        // 3. Check hierarchy: FullControl (0) <= Editor (1) <= Viewer (2).
        // Since userRole is now AppRole (enum), we compare it directly with requiredRole.
        return userRole.Value <= requiredRole;
    }
}
