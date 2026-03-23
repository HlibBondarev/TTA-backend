using TTA.Common.Enums;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Auth;

/// <summary>
/// Specialized repository for handling user access policies and role retrieval.
/// </summary>
public interface IAccessRepository : IEntityRepositoryBase<Guid, AccessPolicy>
{
    /// <summary>
    /// Retrieves the effective user role for a specific target scope or global access from the database.
    /// </summary>
    /// <param name="userId">The unique identifier (sub) of the authenticated user.</param>
    /// <param name="targetScope">The scope of the resource (e.g., Global, Club, or Team).</param>
    /// <param name="targetId">The unique identifier of the specific resource (null for Global scope).</param>
    /// <param name="ct">A token to monitor for managed cancellation requests.</param>
    /// <returns>The name of the assigned <see cref="AppRole"/> if found; otherwise, <c>null</c>.</returns>
    Task<AppRole?> GetUserRoleForScope(
        string userId,
        TargetScope targetScope,
        Guid? targetId,
        CancellationToken ct = default);
}