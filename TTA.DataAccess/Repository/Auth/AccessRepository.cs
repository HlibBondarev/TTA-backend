using Dapper;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Auth;

public class AccessRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, AccessPolicy>(connectionFactory), IAccessRepository
{
    /// <summary>
    /// Retrieves the user's effective role for a specific scope by calling a stored procedure.
    /// </summary>
    public async Task<string?> GetUserRoleForScope(string userId, string targetType, Guid? targetId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_user_id", userId);
        parameters.Add("p_target_type", targetType);
        parameters.Add("p_target_id", targetId);

        return await ExecuteQueryInTransaction<string?>(
            SqlStatements.ForAccessPolicies.GetUserPermission,
            parameters);
    }
}