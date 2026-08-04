using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for managing User entities in PostgreSQL.
/// Inherits from <see cref="EntityRepositoryBase{string, User}"/> for common CRUD operations.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class UserRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<string, User>(connectionFactory), IUserRepository
{
    /// <inheritdoc />
    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        return await GetById(id, SqlStatements.ForUsers.GetUserById, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<User>> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_email", email);

        return await GetByPropValues(
            SqlStatements.ForUsers.GetUsersByEmail,
            parameters,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User> UpsertAsync(User user, CancellationToken cancellationToken = default)
    {
        return await CreateOrUpdate(user, SqlStatements.ForUsers.UpsertUser, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await Delete(id, SqlStatements.ForUsers.DeleteUser, cancellationToken);
    }
}