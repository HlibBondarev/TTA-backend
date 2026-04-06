using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the Team entity.
/// Inherits from <see cref="IEntityRepositoryBase{Guid, Team}"/> for common operations.
/// </summary>
public interface ITeamRepository : IEntityRepositoryBase<Guid, Team>
{
    /// <summary>
    /// Persists a new team or updates an existing one in the database.
    /// </summary>
    /// <param name="team">The team entity to persist.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The persisted <see cref="Team"/> entity.</returns>
    Task<Team> CreateTeamAsync(Team team, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all teams associated with a specific club.
    /// </summary>
    /// <param name="clubId">The unique identifier of the club.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of <see cref="Team"/> entities belonging to the club.</returns>
    Task<IEnumerable<Team>> GetByClubIdAsync(Guid clubId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the team associated with a specific id.
    /// </summary>
    /// <param name="id">The unique identifier of the team.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The <see cref="Team"/> with a specific id if found; otherwise, null.</returns>
    Task<Team?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
