using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for game events using PostgreSQL storage functions.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class GameEventRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, GameEvent>(connectionFactory), IGameEventRepository
{
    /// <inheritdoc />
    public async Task<GameEvent> UpsertAsync(GameEvent entity, CancellationToken cancellationToken = default)
    {
        // Explicitly calling the upsert function via base CreateOrUpdate.
        // This method handles transaction and maps entity properties to @p_ parameters.
        return await CreateOrUpdate(
            entity,
            SqlStatements.ForGameEvents.UpsertEvent,
            null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GameEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Utilizing base GetById with specific SQL constant from ForGameEvents.
        return await GetById(
            id,
            SqlStatements.ForGameEvents.GetById,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GameEventProjection?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", id);

        using var connection = await OpenConnectionAsync(cancellationToken);

        // Returning dynamic to keep repository decoupled from API-level DTOs
        return await connection.QueryFirstOrDefaultAsync<GameEventProjection>(new CommandDefinition(
            SqlStatements.ForGameEvents.GetByIdWithDetails,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<GameEventProjection>> GetMatchEventsAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        // Returns dynamic objects to include joined metadata like PlayerName, TeamName, and PlayerNumber.
        return await connection.QueryAsync<GameEventProjection>(new CommandDefinition(
            SqlStatements.ForGameEvents.GetMatchEvents,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Utilizing base Delete method which returns Task<bool>.
        // It internally uses SqlStatements.ForGameEvents.DeleteEvent and handles the transaction.
        return await Delete(
            id,
            SqlStatements.ForGameEvents.DeleteEvent,
            cancellationToken);
    }
}