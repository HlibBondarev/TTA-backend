using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for managing SportConfiguration entities in PostgreSQL using Dapper.
/// Inherits from <see cref="EntityRepositoryBase{Guid, SportConfiguration}"/> for common CRUD logic.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class SportConfigurationRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, SportConfiguration>(connectionFactory), ISportConfigurationRepository
{
    /// <inheritdoc />
    public async Task<SportConfiguration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetById(id, SqlStatements.ForSportConfigurations.GetSportConfigurationById, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SportConfiguration>> GetBySportIdAsync(Guid sportId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_sport_id", sportId);

        return await GetByPropValues(
            SqlStatements.ForSportConfigurations.GetSportConfigurationsBySportId,
            parameters,
            cancellationToken);
    }
}