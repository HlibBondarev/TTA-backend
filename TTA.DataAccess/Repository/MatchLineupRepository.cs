using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements match lineup management operations using PostgreSQL storage functions.
/// </summary>
public class MatchLineupRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, MatchLineup>(connectionFactory), IMatchLineupRepository
{
    /// <inheritdoc />
    public async Task<MatchLineup> UpsertLineupItemAsync(MatchLineup lineup, CancellationToken cancellationToken = default)
    {
        return await CreateOrUpdate(
            lineup,
            SqlStatements.ForMatchLineups.UpsertLineupItem,
            null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<dynamic>> GetByMatchIdAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryAsync<dynamic>(new CommandDefinition(
            SqlStatements.ForMatchLineups.GetMatchLineup,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteLineupItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Utilizing the base Delete method which handles transactions and ID mapping
        return await Delete(
            id,
            SqlStatements.ForMatchLineups.DeleteLineupItem,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CopyFromRosterAsync(Guid matchId, Guid teamId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_matchid", matchId);
        parameters.Add("p_teamid", teamId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlStatements.ForMatchLineups.CopyRosterToLineup,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<MatchLineup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Using base GetById for single entity retrieval
        return await GetById(
            id,
            SqlStatements.ForMatchLineups.GetById,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<dynamic?> GetMatchLineupByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", id);

        using var connection = await OpenConnectionAsync(cancellationToken);
        // Using dynamic to capture joined fields for the response DTO
        return await connection.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            SqlStatements.ForMatchLineups.GetByIdWithDetails,
            parameters,
            cancellationToken: cancellationToken));
    }
}