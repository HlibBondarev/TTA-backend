using Dapper;
using TTA.Common.Enums;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Auth;

/// <inheritdoc cref="IAccessRepository" />
public class AccessRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, AccessPolicy>(connectionFactory), IAccessRepository
{
    /// <inheritdoc />
    public async Task<AppRole?> GetUserRoleForScope(
        string userId,
        TargetScope targetScope,
        Guid? targetId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("TargetType", targetScope);
        parameters.Add("TargetId", targetId);

        // We use CommandType.Text because SqlStatements contains an explicit SELECT 
        // to correctly invoke the PostgreSQL function.
        var result = await ExecuteQueryInTransaction<int?>(
            SqlStatements.ForAccessPolicies.GetUserPermission,
            parameters,
            cancellationToken: cancellationToken
        );

        return result.HasValue ? (AppRole)result.Value : null;
    }

    /// <inheritdoc />
    public async Task<AccessPolicy> AddAccessAsync(AccessPolicy accessPolicy, CancellationToken cancellationToken = default)
    {
        return await CreateOrUpdate(accessPolicy, SqlStatements.ForAccessPolicies.UpsertAccessPolicy, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessPolicy> RemoveAccessAsync(AccessPolicy accessPolicy, CancellationToken cancellationToken = default)
    {
        // Logic: The caller must set accessPolicy.ExpiresAt = DateTime.UtcNow
        return await CreateOrUpdate(accessPolicy, SqlStatements.ForAccessPolicies.UpsertAccessPolicy, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAccessAsync(
        AccessPolicy accessPolicy,
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(accessPolicy);

        // We use the shared connection and transaction provided by the handler
        await ExecuteCommandAsync(
            SqlStatements.ForAccessPolicies.UpsertAccessPolicy,
            parameters,
            connection,
            transaction,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessPolicy?> GetActiveTeamPolicyAsync(
        string userId,
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("TeamId", teamId);

        // Using GetByPropValues from the base repository to fetch the collection 
        // and returning the first active policy found.
        var results = await GetByPropValues(
            SqlStatements.ForAccessPolicies.GetActiveTeamPolicy,
            parameters,
            cancellationToken);

        return results.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await Delete(id, SqlStatements.ForAccessPolicies.DeleteAccessPolicy, cancellationToken);
    }
}