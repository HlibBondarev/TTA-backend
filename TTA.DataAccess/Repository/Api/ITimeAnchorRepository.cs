using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the TimeAnchor entity.
/// </summary>
public interface ITimeAnchorRepository : IEntityRepositoryBase<Guid, TimeAnchor>
{
    /// <summary>
    /// Persists a time anchor to the database using an upsert operation.
    /// </summary>
    /// <param name="entity">The time anchor entity to save.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The persisted time anchor entity.</returns>
    Task<TimeAnchor> UpsertAsync(TimeAnchor entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single time anchor by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the time anchor.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The time anchor entity if found; otherwise, null.</returns>
    Task<TimeAnchor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all time anchors associated with a specific match.
    /// Results are returned in chronological order as defined by the storage function.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of time anchor entities.</returns>
    Task<IEnumerable<TimeAnchor>> GetMatchAnchorsAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a time anchor from the database by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the anchor to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. Returns true if the operation was successful.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}