using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access contract for Player entities.
/// </summary>
public interface IPlayerRepository : IEntityRepositoryBase<Guid, Player>
{
    /// <summary>
    /// Retrieves all players associated with a specific club.
    /// </summary>
    /// <param name="clubId">The unique identifier of the club.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of players.</returns>
    Task<IEnumerable<Player>> GetByClubIdAsync(Guid clubId, CancellationToken ct);

    /// <summary>
    /// Creates a new player entry in the database.
    /// </summary>
    /// <param name="player">The player entity to create.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The created player entity as returned by the database.</returns>
    Task<Player> CreatePlayerAsync(Player player, CancellationToken ct);
}
