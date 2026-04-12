using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines specialized data access operations for the <see cref="Tournament"/> entity.
/// </summary>
public interface ITournamentRepository : IEntityRepositoryBase<Guid, Tournament>
{
    /// <summary>
    /// Creates a new tournament or updates an existing one using a specialized database storage function.
    /// </summary>
    /// <param name="entity">The tournament entity to persist.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, containing the persisted tournament record.</returns>
    Task<Tournament> CreateOrUpdate(Tournament entity, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a tournament by its unique identifier using a database storage function.
    /// </summary>
    /// <param name="id">The unique identifier of the tournament.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, containing the tournament if found; otherwise, null.</returns>
    Task<Tournament?> GetByIdAsync(Guid id, CancellationToken ct = default);
}