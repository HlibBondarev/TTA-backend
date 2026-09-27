using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the GameEvent entity.
/// </summary>
public interface IGameEventRepository : IEntityRepositoryBase<Guid, GameEvent>
{
    /// <summary>
    /// Persists a batch of game events to the database using a JSONB upsert operation.
    /// </summary>
    /// <param name="entities">The collection of game event entities to save.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The collection of persisted game event entities.</returns>
    Task<IEnumerable<GameEvent>> UpsertAsync(IEnumerable<GameEvent> entities, CancellationToken cancellationToken = default);

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
    /// <returns>A projection object containing the raw record details if found.</returns>
    Task<GameEventProjection?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all events associated with a specific match, enriched with player and team metadata.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of projection objects representing the match timeline.</returns>
    Task<IEnumerable<GameEventProjection>> GetMatchEventsAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a game event from the database by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the game event to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. Returns true if the operation was successful.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the batch storage function to recalculate and update the normalized match time 
    /// for all game events belonging to a specific team in a match.
    /// </summary>
    /// <param name="matchId">The unique database reference key for the target match.</param>
    /// <param name="teamId">The unique database reference key for the target team.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task NormalizeMatchEventsTimeAsync(Guid matchId, Guid teamId, CancellationToken cancellationToken = default);
}