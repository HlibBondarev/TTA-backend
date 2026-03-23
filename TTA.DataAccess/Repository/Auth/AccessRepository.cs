using Dapper;
using System.Data;
using TTA.Common.Enums;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Auth;

public class AccessRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, AccessPolicy>(connectionFactory), IAccessRepository
{
    /// <summary>
    /// Retrieves the specific application role assigned to a user within a given scope (Club, Team, or Global).
    /// This method invokes a PostgreSQL function via a SELECT statement.
    /// </summary>
    /// <param name="userId">The unique identifier (Subject) of the user from the identity provider.</param>
    /// <param name="scope">The target scope level (e.g., Club, Team, Global) to check permissions for.</param>
    /// <param name="resourceId">The optional unique identifier of the specific resource (Club ID or Team ID).</param>
    /// <returns>
    /// A task that represents the asynchronous operation. 
    /// The task result contains the <see cref="AppRole"/> if found; otherwise, <see langword="null"/>.
    /// </returns>
    public async Task<AppRole?> GetUserRoleForScope(string userId, TargetScope scope, Guid? resourceId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("Scope", (int)scope);
        parameters.Add("ResourceId", resourceId);

        // We use CommandType.Text because the constant contains an explicit SELECT statement 
        // to correctly invoke the PostgreSQL function.
        var result = await ExecuteQueryInTransaction<int?>(
            SqlStatements.ForAccessPolicies.GetUserPermission,
            parameters,
            commandType: CommandType.Text
        );

        return result.HasValue ? (AppRole)result.Value : null;
    }
}