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
    /// Creates a new player or updates an existing one using a stored procedure.
    /// Mapping of Gender (Enum) to String is handled within the database function.
    /// </summary>
    /// <param name="player">The player entity to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="Player"/> entity.</returns>
    public async Task<Player> CreatePlayerAsync(Player player, CancellationToken ct)
    {
        // We only need to handle the Enum-to-int conversion here.
        // DateOnly is handled globally by the DateOnlyTypeHandler we registered.
        var parameters = new DynamicParameters(player);
        parameters.Add("Gender", (int)player.Gender);

        return await CreateOrUpdate(
            player,
            SqlStatements.ForPlayers.UpsertPlayer,
            parameters,
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