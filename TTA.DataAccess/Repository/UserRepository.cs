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
    public async Task<User?> GetByIdAsync(string id, CancellationToken ct)
    {
        return await GetById(id, SqlStatements.ForUsers.GetUserById, ct);
    }
}