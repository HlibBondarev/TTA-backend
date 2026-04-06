using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for managing Player entities in PostgreSQL.
/// Inherits from <see cref="EntityRepositoryBase{Guid, Player}"/> for common CRUD operations.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class PlayerRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, Player>(connectionFactory), IPlayerRepository
{
    /// <inheritdoc />
    public async Task<Player> CreatePlayerAsync(Player player, CancellationToken ct)
    {
        // No manual parameter mapping needed for Gender. 
        // Dapper maps all properties of the 'player' object (including Enum as int)
        // to the @parameters in SqlStatements.ForPlayers.UpsertPlayer.
        return await CreateOrUpdate(
            player,
            SqlStatements.ForPlayers.UpsertPlayer,
            new DynamicParameters(player),
            ct);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Player>> GetByClubIdAsync(Guid clubId, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_club_id", clubId);

        return await GetByPropValues(SqlStatements.ForPlayers.GetPlayersByClub, parameters, ct);
    }

    /// <inheritdoc />
    public async Task<Player?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await GetById(id, SqlStatements.ForPlayers.GetPlayerById, ct);
    }
}