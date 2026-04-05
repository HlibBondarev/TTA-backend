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
}