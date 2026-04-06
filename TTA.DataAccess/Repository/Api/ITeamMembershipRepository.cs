using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the TeamMembership entity.
/// Inherits from <see cref="IEntityRepositoryBase{Guid, TeamMembership}"/> for common operations.
/// </summary>
public interface ITeamMembershipRepository : IEntityRepositoryBase<Guid, TeamMembership>
{
    /// <summary>
    /// Persists a new team membership or updates an existing one using a stored function.
    /// </summary>
    /// <param name="membership">The membership entity to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="TeamMembership"/> entity as returned by the database.</returns>
    Task<TeamMembership> CreateMembershipAsync(TeamMembership membership, CancellationToken ct = default);

    /// <summary>
    /// Terminates an active membership by setting the departure date and resetting the primary flag.
    /// Utilizes a soft-delete approach via an UPDATE statement.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="membershipId">The unique identifier of the membership to terminate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the membership was successfully terminated; otherwise, <c>false</c>.</returns>
    Task<bool> TerminateMembershipAsync(Guid teamId, Guid membershipId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all active team members as a JSON string for flexible DTO mapping.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A collection of active <see cref="TeamMembership"/> entities in json-format.</returns>
    Task<string?> GetMembersJsonAsync(Guid teamId, CancellationToken ct = default);
}