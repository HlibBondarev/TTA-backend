using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines specialized data access operations for tournament player rosters.
/// </summary>
public interface IRosterRepository : IEntityRepositoryBase<Guid, PlayerRoster>
{
    /// <summary>
    /// Adds or updates a player entry in a tournament roster using a database storage function.
    /// </summary>
    /// <param name="roster">The roster entity containing assignment details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation, returning the persisted <see cref="PlayerRoster"/>.</returns>
    Task<PlayerRoster> UpsertRosterItemAsync(PlayerRoster roster, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all roster entries for a specific team in a tournament.
    /// </summary>
    /// <param name="tournamentId">Tournament identifier.</param>
    /// <param name="teamId">Team identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of roster items with player and position details.</returns>
    Task<IEnumerable<dynamic>> GetTeamRosterAsync(Guid tournamentId, Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a player from a tournament roster.
    /// </summary>
    /// <param name="tournamentId">Tournament identifier.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="playerId">Player identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemovePlayerFromRosterAsync(Guid tournamentId, Guid teamId, Guid playerId, CancellationToken cancellationToken = default);
}