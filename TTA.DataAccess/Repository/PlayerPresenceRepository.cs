using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

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
    public async Task<Guid> RecordSubstitutionAsync(PlayerPresence outgoingPresence, PlayerPresence incomingPresence, CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Update the outgoing player's presence
            await connection.ExecuteAsync(new CommandDefinition(
                SqlStatements.ForPlayerPresence.RecordPresence,
                outgoingPresence,
                transaction: transaction,
                cancellationToken: cancellationToken));

            // 2. Insert the incoming player's presence
            await connection.ExecuteAsync(new CommandDefinition(
                SqlStatements.ForPlayerPresence.RecordPresence,
                incomingPresence,
                transaction: transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
            return incomingPresence.Id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
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
    public async Task InitializePeriodPresenceAsync(
        int periodNumber,
        DateTime timeIn,
        IEnumerable<(Guid Id, Guid LineupId)> presences,
        CancellationToken cancellationToken = default)
    {
        var presenceList = presences.ToList();

        var parameters = new DynamicParameters();
        parameters.Add("p_period_number", periodNumber);
        parameters.Add("p_time_in", timeIn);
        parameters.Add("p_ids", presenceList.Select(x => x.Id).ToArray());
        parameters.Add("p_lineup_ids", presenceList.Select(x => x.LineupId).ToArray());

        using var connection = await OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlStatements.ForPlayerPresence.InitializePeriodPresence,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<PlayersDirtyTimeByPeriodProjection>> GetPlayersDirtyTimeByPeriodAsync(Guid matchId, Guid teamId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_team_id", teamId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryAsync<PlayersDirtyTimeByPeriodProjection>(new CommandDefinition(
            SqlStatements.ForPlayerPresence.CalculatePlayersDirtyTimeByPeriod,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task CloseActivePresencesAsync(
        Guid matchId,
        int periodNumber,
        DateTime timeOut,
        IEnumerable<Guid>? playerLineupIds = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_period_number", periodNumber);
        parameters.Add("p_time_out", timeOut);
        parameters.Add("p_lineup_ids", playerLineupIds?.ToArray());

        using var connection = await OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlStatements.ForPlayerPresence.CloseActivePresences,
            parameters,
            cancellationToken: cancellationToken));
    }
}