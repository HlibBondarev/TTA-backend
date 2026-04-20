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
}