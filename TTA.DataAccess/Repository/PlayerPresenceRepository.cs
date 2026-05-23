using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for player presence using PostgreSQL storage functions.
/// Directly maps business requirements to database execution.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class PlayerPresenceRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, PlayerPresence>(connectionFactory), IPlayerPresenceRepository
{
    /// <inheritdoc />
    public async Task<Guid> RecordPresenceAsync(PlayerPresence entity, CancellationToken cancellationToken = default)
    {
        // Executes the upsert function via base CreateOrUpdate.
        var result = await CreateOrUpdate(
            entity,
            SqlStatements.ForPlayerPresence.RecordPresence,
            null,
            cancellationToken);

        return result.Id;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<PlayerPresence>> GetMatchPresenceAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<PlayerPresence>(new CommandDefinition(
            SqlStatements.ForPlayerPresence.GetMatchPresence,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task InitializePeriodPresenceAsync(int periodNumber, DateTime timeIn, IEnumerable<Guid> lineupIds, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_period_number", periodNumber);
        parameters.Add("p_time_in", timeIn);
        parameters.Add("p_lineup_ids", lineupIds.ToArray()); // Dapper maps IEnumerable to PostgreSQL array automatically

        using var connection = await OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlStatements.ForPlayerPresence.InitializePeriodPresence,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task CloseActivePresencesAsync(Guid matchId, int periodNumber, DateTime timeOut, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_period_number", periodNumber);
        parameters.Add("p_time_out", timeOut);

        using var connection = await OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlStatements.ForPlayerPresence.CloseActivePresences,
            parameters,
            cancellationToken: cancellationToken));
    }
}