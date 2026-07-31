using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for managing Sport entities in PostgreSQL using Dapper.
/// Inherits from <see cref="EntityRepositoryBase{Guid, Sport}"/> for common CRUD logic.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class SportRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, Sport>(connectionFactory), ISportRepository
{
    /// <inheritdoc />
    public async Task<Sport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetById(id, SqlStatements.ForSports.GetSportById, cancellationToken);
    }
}