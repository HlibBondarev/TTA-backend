using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

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
        // Explicitly calling base CreateOrUpdate with the specific entity type
        return await CreateOrUpdate(match, SqlStatements.ForMatches.UpsertMatch, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Match?> GetByIdAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        // Using base GetById for single entity retrieval by primary key
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
        // We use dynamic to capture extra fields like Team Names for the DTO
        return await connection.QueryAsync<dynamic>(new CommandDefinition(
            SqlStatements.ForMatches.GetTournamentMatches,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    /// <summary>
    /// Executes the 'GetMatchWithDetailsById' SQL statement to fetch a match with its associated metadata.
    /// Uses Dapper's QueryFirstOrDefaultAsync with a dynamic return type.
    /// </summary>
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
}