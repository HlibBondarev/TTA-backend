using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Provides PostgreSQL implementation for tournament data access using Dapper and storage functions.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class TournamentRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, Tournament>(connectionFactory), ITournamentRepository
{
    /// <inheritdoc />
    public async Task<Tournament> CreateOrUpdate(Tournament entity, CancellationToken ct = default)
    {
        // Reusing the base CreateOrUpdate logic by passing the specific SQL statement for tournaments.
        return await base.CreateOrUpdate(
            entity,
            SqlStatements.ForTournaments.UpsertTournament,
            null,
            ct);
    }

    /// <inheritdoc />
    public async Task<Tournament?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = GetConnection();

        var parameters = new DynamicParameters();
        parameters.Add("p_id", id);

        return await connection.QueryFirstOrDefaultAsync<Tournament>(
            new CommandDefinition(
                SqlStatements.ForTournaments.GetTournamentById,
                parameters,
                cancellationToken: ct)
        );
    }
}