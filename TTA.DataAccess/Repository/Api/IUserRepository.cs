using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access contract for Player entities.
/// </summary>
public interface IUserRepository : IEntityRepositoryBase<string, User>
{
    /// <summary>
    /// Retrieves a user by their unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the user.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The user entity if found; otherwise, null.</returns>
    Task<User?> GetByIdAsync(string id, CancellationToken ct);
}