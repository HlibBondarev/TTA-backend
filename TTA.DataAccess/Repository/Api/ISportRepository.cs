using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the Sport entity.
/// Inherits from <see cref="IEntityRepositoryBase{Guid, Sport}"/> for common operations.
/// </summary>
public interface ISportRepository : IEntityRepositoryBase<Guid, Sport>
{
    /// <summary>
    /// Retrieves the sport associated with a specific identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the sport.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <see cref="Sport"/> entity if found; otherwise, null.</returns>
    Task<Sport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}