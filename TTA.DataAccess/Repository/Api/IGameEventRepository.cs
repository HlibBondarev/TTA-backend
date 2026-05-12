using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the GameEvent entity.
/// </summary>
public interface IGameEventRepository : IEntityRepositoryBase<Guid, GameEvent>
{
    /// <summary>
    /// Persists a game event to the database using an upsert operation.
    /// </summary>
    /// <param name="entity">The game event entity to save.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The persisted game event entity.</returns>
    Task<GameEvent> UpsertAsync(GameEvent entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single game event by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the game event.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The game event entity if found; otherwise, null.</returns>
    Task<GameEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves raw detailed data for a specific game event from the database.
    /// </summary>
    /// <param name="id">The unique identifier of the game event.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dynamic object containing the raw record details if found.</returns>
    Task<dynamic?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all events associated with a specific match, enriched with player and team metadata.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of dynamic objects representing the match timeline.</returns>
    Task<IEnumerable<dynamic>> GetMatchEventsAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a game event from the database by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the game event to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. Returns true if the operation was successful.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}