using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the SportConfiguration entity.
/// Inherits from <see cref="IEntityRepositoryBase{Guid, SportConfiguration}"/> for common operations.
/// </summary>
public interface ISportConfigurationRepository : IEntityRepositoryBase<Guid, SportConfiguration>
{
    /// <summary>
    /// Retrieves the sport configuration associated with a specific identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the sport configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <see cref="SportConfiguration"/> entity if found; otherwise, null.</returns>
    Task<SportConfiguration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}