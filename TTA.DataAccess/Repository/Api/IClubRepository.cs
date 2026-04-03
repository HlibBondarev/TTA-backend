using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access contract for Club entities using Dapper and PostgreSQL functions.
/// </summary>
public interface IClubRepository : IEntityRepositoryBase<Guid, Club>
{
    /// <summary>
    /// Checks if the user already has a 'FullControl' role for any Club in the system.
    /// Uses a dedicated database function for optimized performance.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains true if the user owns at least one club; otherwise, false.</returns>
    Task<bool> HasExistingClubOwnershipAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Checks if the user has a 'FullControl' role for a specific Club instance.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="clubId">The unique identifier of the club.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains true if ownership exists for the specific club; otherwise, false.</returns>
    Task<bool> HasClubOwnershipAsync(string userId, Guid clubId, CancellationToken ct);

    /// <summary>
    /// Atomically creates a club and its initial access policy via a database function.
    /// </summary>
    /// <param name="club">The club entity to create.</param>
    /// <param name="userId">The unique identifier of the user who will own the club.</param>
    /// <param name="userEmail">The email of the user who will own the club.</param>
    /// <param name="userName">The name of the user who will own the club.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created club's identifier.</returns>
    Task<Guid> CreateWithOwnershipAsync(Club club, string userId, string userEmail, string userName, CancellationToken ct);
}