using Dapper;
using System.Text.Json;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for time anchors using PostgreSQL storage functions and JSONB.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class TimeAnchorRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, TimeAnchor>(connectionFactory), ITimeAnchorRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<TimeAnchor>> UpsertAsync(IEnumerable<TimeAnchor> entities, CancellationToken cancellationToken = default)
    {
        var list = entities.ToList();
        if (list.Count == 0) return [];

        var jsonPayload = JsonSerializer.Serialize(list.Select(e => new
        {
            id = e.Id,
            matchid = e.MatchId,
            periodnumber = e.PeriodNumber,
            type = (int)e.Type,
            timestamp = e.Timestamp
        }));

        var parameters = new DynamicParameters();
        parameters.Add("p_anchors", jsonPayload);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<TimeAnchor>(new CommandDefinition(
            SqlStatements.ForTimeAnchors.UpsertTimeAnchor,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<TimeAnchor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetById(id, SqlStatements.ForTimeAnchors.GetById, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TimeAnchor>> GetMatchAnchorsAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<TimeAnchor>(new CommandDefinition(
            SqlStatements.ForTimeAnchors.GetMatchAnchors,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await Delete(id, SqlStatements.ForTimeAnchors.DeleteAnchor, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetMatchPeriodDurationMinutesAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);

        return await ExecuteQueryInTransaction<int>(
            SqlStatements.ForTimeAnchors.GetMatchPeriodDuration,
            parameters,
            cancellationToken);
    }
}