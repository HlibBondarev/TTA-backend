using Dapper;
using System.Text.Json;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for game events using PostgreSQL storage functions and JSONB.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class GameEventRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, GameEvent>(connectionFactory), IGameEventRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<GameEvent>> UpsertAsync(IEnumerable<GameEvent> entities, CancellationToken cancellationToken = default)
    {
        var list = entities.ToList();
        if (list.Count == 0) return [];

        var jsonPayload = JsonSerializer.Serialize(list.Select(e => new
        {
            id = e.Id,
            matchlineupid = e.MatchLineupId,
            eventdefinitionid = e.EventDefinitionId,
            periodnumber = e.PeriodNumber,
            eventtimestamp = e.EventTimestamp,
            normalizedmatchtime = e.NormalizedMatchTime,
            isleadtogoal = e.IsLeadToGoal,
            createdat = e.CreatedAt
        }));

        var parameters = new DynamicParameters();
        parameters.Add("p_events", jsonPayload);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<GameEvent>(new CommandDefinition(
            SqlStatements.ForGameEvents.UpsertEvent,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<GameEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetById(id, SqlStatements.ForGameEvents.GetById, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GameEventProjection?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", id);

        using var connection = await OpenConnectionAsync(cancellationToken);

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

        return await connection.QueryAsync<GameEventProjection>(new CommandDefinition(
            SqlStatements.ForGameEvents.GetMatchEvents,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await Delete(id, SqlStatements.ForGameEvents.DeleteEvent, cancellationToken);
    }

    /// <inheritdoc />
    public async Task NormalizeMatchEventsTimeAsync(Guid matchId, Guid teamId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_team_id", teamId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteCommandAsync(
                SqlStatements.ForGameEvents.NormalizeMatchEventsTime,
                parameters,
                connection,
                transaction,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}