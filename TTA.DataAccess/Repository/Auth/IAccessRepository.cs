using TTA.Common.Enums;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Auth;

/// <summary>
/// Provides methods for managing user access policies, including granting and revoking permissions.
/// </summary>
public interface IAccessRepository : IEntityRepositoryBase<Guid, AccessPolicy>
{
    /// <summary>
    /// Retrieves the effective user role for a specific target scope or global access from the database.
    /// </summary>
    /// <param name="userId">The unique identifier (sub) of the authenticated user.</param>
    /// <param name="targetScope">The scope of the resource (e.g., Global, Club, or Team).</param>
    /// <param name="targetId">The unique identifier of the specific resource (null for Global scope).</param>
    /// <param name="cancellationToken">A token to monitor for managed cancellation requests.</param>
    /// <returns>The name of the assigned <see cref="AppRole"/> if found; otherwise, <c>null</c>.</returns>
    Task<AppRole?> GetUserRoleForScope(
        string userId,
        TargetScope targetScope,
        Guid? targetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a new access policy or updates an existing one.
    /// </summary>
    /// <param name="accessPolicy">The access policy entity to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted <see cref="AccessPolicy"/>.</returns>
    Task<AccessPolicy> AddAccessAsync(AccessPolicy accessPolicy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an access policy by setting its expiration date.
    /// Access is considered revoked if <c>ExpiresAt</c> is less than or equal to current time.
    /// </summary>
    /// <param name="accessPolicy">The policy entity with an updated expiration timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated <see cref="AccessPolicy"/>.</returns>
    Task<AccessPolicy> RemoveAccessAsync(AccessPolicy accessPolicy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an access policy by setting its expiration date using an existing transaction.
    /// Access is considered revoked if <c>ExpiresAt</c> is less than or equal to current time.
    /// </summary>
    /// <param name="accessPolicy">The policy entity with an updated expiration timestamp.</param>
    /// <param name="connection">An existing and open database connection.</param>
    /// <param name="transaction">An active transaction associated with the provided connection.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveAccessAsync(
        AccessPolicy accessPolicy,
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for an active (non-expired) access policy for a specific user and team.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active <see cref="AccessPolicy"/> if found; otherwise, <c>null</c>.</returns>
    Task<AccessPolicy?> GetActiveTeamPolicyAsync(string userId, Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes an access policy record by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the access policy to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task returning true if the record was successfully deleted; otherwise, false.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}