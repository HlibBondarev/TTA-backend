using TTA.Common.Enums;

namespace TTA.BusinessLogic.Services.Api;

public interface IAccessService
{
    /// <summary>
    /// Checks if the user has the required permission level for a specific resource or global scope.
    /// </summary>
    /// <param name="userId">Unique identifier of the user (Auth0 sub).</param>
    /// <param name="requiredRole">Minimum role required for the operation.</param>
    /// <param name="targetType">The scope of access (Global, Club, or Team).</param>
    /// <param name="targetId">Specific ID of the resource (null for Global scope).</param>
    /// <returns>True if access is granted; otherwise, false.</returns>
    Task<bool> HasAccessAsync(
        string userId,
        AppRole requiredRole,
        TargetScope targetType,
        Guid? targetId = null,
        CancellationToken ct = default);
}
