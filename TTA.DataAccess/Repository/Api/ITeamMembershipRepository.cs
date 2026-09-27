using TTA.Common.Enums;
using TTA.DataAccess.Enums;
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
    /// Creates or updates a team membership and its associated access policy atomically.
    /// </summary>
    /// <param name="membership">The membership entity to save.</param>
    /// <param name="appRole">The Role in App with type AppRole enum.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted <see cref="TeamMembership"/> entity as returned by the database.</returns>
    Task<TeamMembership> CreateMembershipWithPolicyAsync(TeamMembership membership, AppRole appRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active team members as a JSON string for flexible DTO mapping.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of active <see cref="TeamMembership"/> entities in json-format.</returns>
    Task<string?> GetMembersJsonAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logically terminates a membership by setting the <c>LeftAt</c> timestamp.
    /// </summary>
    /// <param name="teamMembership">The membership entity to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated <see cref="TeamMembership"/>.</returns>
    Task<TeamMembership> TerminateMembershipAsync(TeamMembership teamMembership, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logically terminates a membership by setting the <c>LeftAt</c> timestamp using an existing transaction.
    /// This ensures atomic execution when coordinated with other repository actions.
    /// </summary>
    /// <param name="teamMembership">The membership entity to update.</param>
    /// <param name="connection">An existing and open database connection.</param>
    /// <param name="transaction">An active transaction associated with the provided connection.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task TerminateMembershipAsync(
        TeamMembership teamMembership,
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active memberships (where <c>LeftAt</c> is null) for a specific user email within a team.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="userEmail">The email address of the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of active <see cref="TeamMembership"/> records.</returns>
    Task<IEnumerable<TeamMembership>> GetActiveMembershipsByEmailAsync(Guid teamId, string userEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific active membership (where <c>LeftAt</c> is null) for a user email 
    /// and specific role within a team.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="userEmail">The email address of the user.</param>
    /// <param name="teamRole">The specific role in the team to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active <see cref="TeamMembership"/> record, or null if not found.</returns>
    Task<TeamMembership?> GetActiveMembershipByEmailAndRoleAsync(Guid teamId, string userEmail, TeamRole teamRole, CancellationToken cancellationToken = default);
}