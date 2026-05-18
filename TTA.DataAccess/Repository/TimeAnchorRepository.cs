using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for time anchors using PostgreSQL storage functions.
/// Supports piecewise-linear time normalization logic.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class TimeAnchorRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, TimeAnchor>(connectionFactory), ITimeAnchorRepository
{
    /// <inheritdoc />
    public async Task<TimeAnchor> UpsertAsync(TimeAnchor entity, CancellationToken cancellationToken = default)
    {
        // Executes the upsert function via base CreateOrUpdate.
        // Parameters are mapped automatically from the entity properties to @Property names.
        return await CreateOrUpdate(
            entity,
            SqlStatements.ForTimeAnchors.UpsertTimeAnchor,
            null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TimeAnchor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Utilizing base GetById with the specific SQL constant for time anchors.
        return await GetById(
            id,
            SqlStatements.ForTimeAnchors.GetById,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TimeAnchor>> GetMatchAnchorsAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        // Retrieves all anchors for a match to build the time normalization timeline.
        return await connection.QueryAsync<TimeAnchor>(new CommandDefinition(
            SqlStatements.ForTimeAnchors.GetMatchAnchors,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Utilizing base Delete method which handles the transaction and calls the storage function.
        return await Delete(
            id,
            SqlStatements.ForTimeAnchors.DeleteAnchor,
            cancellationToken);
    }
}