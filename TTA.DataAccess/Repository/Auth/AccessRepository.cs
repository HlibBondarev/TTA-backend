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
        CancellationToken ct = default)
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
            ct: ct
        );

        return result.HasValue ? (AppRole)result.Value : null;
    }

    /// <inheritdoc />
    public async Task<AccessPolicy> AddAccessAsync(AccessPolicy accessPolicy, CancellationToken ct = default)
    {
        return await CreateOrUpdate(accessPolicy, SqlStatements.ForAccessPolicies.UpsertAccessPolicy, null, ct);
    }

    /// <inheritdoc />
    public async Task<AccessPolicy> RemoveAccessAsync(AccessPolicy accessPolicy, CancellationToken ct = default)
    {
        // Logic: The caller must set accessPolicy.ExpiresAt = DateTime.UtcNow
        return await CreateOrUpdate(accessPolicy, SqlStatements.ForAccessPolicies.UpsertAccessPolicy, null, ct);
    }

    /// <inheritdoc />
    public async Task<AccessPolicy?> GetActiveTeamPolicyAsync(
        string userId,
        Guid teamId,
        CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("TeamId", teamId);

        // Using GetByPropValues from the base repository to fetch the collection 
        // and returning the first active policy found.
        var results = await GetByPropValues(
            SqlStatements.ForAccessPolicies.GetActiveTeamPolicy,
            parameters,
            ct);

        return results.FirstOrDefault();
    }
}