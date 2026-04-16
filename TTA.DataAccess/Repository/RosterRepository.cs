using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements roster management operations for PostgreSQL using Dapper.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class RosterRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, PlayerRoster>(connectionFactory), IRosterRepository
{
    /// <inheritdoc />
    public async Task<PlayerRoster> UpsertRosterItemAsync(PlayerRoster roster, CancellationToken ct = default)
    {
        return await CreateOrUpdate(roster, SqlStatements.ForRosters.UpsertPlayerToRoster, null, ct);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<dynamic>> GetTeamRosterAsync(Guid tournamentId, Guid teamId, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("TournamentId", tournamentId);
        parameters.Add("TeamId", teamId);

        using var connection = await OpenConnectionAsync(ct);
        return await connection.QueryAsync<dynamic>(new CommandDefinition(
            SqlStatements.ForRosters.GetTournamentTeamRoster, parameters, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task RemovePlayerFromRosterAsync(
        Guid tournamentId,
        Guid teamId,
        Guid playerId,
        CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("TournamentId", tournamentId);
        parameters.Add("TeamId", teamId);
        parameters.Add("PlayerId", playerId);

        using var connection = await OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlStatements.ForRosters.RemovePlayerFromRoster, parameters, cancellationToken: ct));
    }
}