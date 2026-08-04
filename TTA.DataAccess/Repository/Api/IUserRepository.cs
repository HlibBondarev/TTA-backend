using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access contract for User entities.
/// </summary>
public interface IUserRepository : IEntityRepositoryBase<string, User>
{
    /// <summary>
    /// Retrieves a user by their unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user entity if found; otherwise, null.</returns>
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all users associated with a specific email.
    /// </summary>
    /// <param name="email">The user email in DB.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of <see cref="User"/> entities with a specific email.</returns>
    Task<IEnumerable<User>> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a user entity to the database using an upsert operation and returns insertion provenance.
    /// </summary>
    /// <param name="user">The user entity to save or update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A tuple containing the persisted user entity and a boolean indicating whether a new row was inserted.</returns>
    Task<(User User, bool IsInserted)> UpsertAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user record by their unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the user to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task returning true if the record was successfully deleted; otherwise, false.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}