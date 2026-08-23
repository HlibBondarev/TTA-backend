using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements match management operations using PostgreSQL storage functions and Base Repository.
/// </summary>
public class MatchRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, Match>(connectionFactory), IMatchRepository
{
    /// <inheritdoc />
    public async Task<Match> UpsertMatchAsync(Match match, CancellationToken cancellationToken = default)
    {
        return await CreateOrUpdate(match, SqlStatements.ForMatches.UpsertMatch, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Match?> GetByIdAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        return await GetById(
            matchId,
            SqlStatements.ForMatches.GetMatchById,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<dynamic>> GetByTournamentIdAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_tournament_id", tournamentId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryAsync<dynamic>(new CommandDefinition(
            SqlStatements.ForMatches.GetTournamentMatches,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<dynamic?> GetMatchByIdWithDetailsAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            SqlStatements.ForMatches.GetMatchWithDetailsById,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Match?> CreateQuickMatchAsync(
        Guid sportId,
        string userId,
        Guid? configurationId = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SportId", sportId);
        parameters.Add("UserId", userId);
        parameters.Add("ConfigurationId", configurationId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Match>(new CommandDefinition(
            SqlStatements.ForMatches.CreateQuickMatch,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlStatements.ForMatches.DeleteMatch,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TeamMatchSummaryReportProjection>> GetTeamSummaryReportAsync(Guid matchId, Guid teamId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_team_id", teamId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryAsync<TeamMatchSummaryReportProjection>(new CommandDefinition(
            SqlStatements.ForMatches.GetTeamSummaryReport,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<PlayerDetailedReportProjection>> GetPlayerDetailedReportAsync(Guid matchId, Guid matchLineupId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_match_lineup_id", matchLineupId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryAsync<PlayerDetailedReportProjection>(new CommandDefinition(
            SqlStatements.ForMatches.GetPlayerDetailedReport,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> CatchMatchAsync(Guid matchId, Guid teamId, string userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_user_id", userId);
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_team_id", teamId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlStatements.ForMatches.CatchUserMatch,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> UncatchMatchAsync(Guid matchId, Guid teamId, string userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_user_id", userId);
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_team_id", teamId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlStatements.ForMatches.UncatchUserMatch,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> IsMatchCatchedByUserAsync(Guid matchId, Guid teamId, string userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_user_id", userId);
        parameters.Add("p_match_id", matchId);
        parameters.Add("p_team_id", teamId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlStatements.ForMatches.IsMatchCatchedByUser,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MatchWithDetailsProjection>> GetCatchedMatchesByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_user_id", userId);

        using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QueryAsync<MatchWithDetailsProjection>(new CommandDefinition(
            SqlStatements.ForMatches.GetUserCatchedMatches,
            parameters,
            cancellationToken: cancellationToken));
    }
}