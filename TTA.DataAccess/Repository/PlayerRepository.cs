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
    /// <summary>
    /// Asynchronously creates or updates a player record in the database.
    /// </summary>
    /// <param name="player">The player entity to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="Player"/> entity as returned by the database.</returns>
    /// <remarks>
    /// This method executes the <see cref="SqlStatements.ForPlayers.UpsertPlayer"/> stored function.
    /// Dapper automatically serializes the <see cref="Player.Gender"/> enum to its underlying integer value,
    /// matching the INT column with a CHECK constraint in the database.
    /// </remarks>
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

    /// <summary>
    /// Retrieves all players associated with a specific club.
    /// </summary>
    /// <param name="clubId">The unique identifier of the club.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A collection of <see cref="Player"/> entities.</returns>
    public async Task<IEnumerable<Player>> GetByClubIdAsync(Guid clubId, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_club_id", clubId);

        return await GetByPropValues(SqlStatements.ForPlayers.GetPlayersByClub, parameters, ct);
    }

    /// <summary>
    /// Retrieves a specific player by their unique identifier.
    /// </summary>
    /// <param name="id">The player's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="Player"/> if found; otherwise, null.</returns>
    public async Task<Player?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await GetById(id, SqlStatements.ForPlayers.GetPlayerById, ct);
    }
}