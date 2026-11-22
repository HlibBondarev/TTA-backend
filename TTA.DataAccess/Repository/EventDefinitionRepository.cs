using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for event definitions using PostgreSQL storage functions.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class EventDefinitionRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, EventDefinition>(connectionFactory), IEventDefinitionRepository
{
    /// <inheritdoc />
    public async Task<EventDefinition?> UpsertCustomAsync(EventDefinition entity, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add(SqlParameters.Id, entity.Id);
        parameters.Add(SqlParameters.SportId, entity.SportId);
        parameters.Add(SqlParameters.OwnerId, entity.OwnerId);
        parameters.Add(SqlParameters.Name, entity.Name);
        parameters.Add(SqlParameters.ShortName, entity.ShortName);
        parameters.Add(SqlParameters.IsPositive, entity.IsPositive);
        parameters.Add(SqlParameters.IsShot, entity.IsShot);
        parameters.Add(SqlParameters.Score, entity.Score);
        parameters.Add(SqlParameters.FoulImpact, (int)entity.FoulImpact);
        parameters.Add(SqlParameters.PenaltyDurationSeconds, entity.PenaltyDurationSeconds);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<EventDefinition>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.UpsertCustomEventDefinition,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(Guid id, string userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add(SqlParameters.Id, id);
        parameters.Add(SqlParameters.UserId, userId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.SoftDeleteEventDefinition,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserEventDefinitionProjection>> GetAvailableForUserAsync(string userId, Guid sportId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add(SqlParameters.UserId, userId);
        parameters.Add(SqlParameters.SportId, sportId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<UserEventDefinitionProjection>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.GetUserAvailableEventDefinitions,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserEventDefinitionProjection>> GetMatchEventDefinitionsAsync(Guid matchId, string? userId = null, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add(SqlParameters.MatchId, matchId);
        parameters.Add(SqlParameters.UserId, userId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<UserEventDefinitionProjection>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.GetMatchEventDefinitions,
            parameters,
            cancellationToken: cancellationToken));
    }
}