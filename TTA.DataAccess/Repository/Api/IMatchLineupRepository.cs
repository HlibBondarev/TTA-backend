using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the Match Lineup (Protocol) entity.
/// </summary>
public interface IMatchLineupRepository : IEntityRepositoryBase<Guid, MatchLineup>
{
    /// <summary>
    /// Persists a match lineup entry to the database using an upsert operation.
    /// </summary>
    /// <param name="lineup">The lineup entry to save.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The persisted match lineup entity.</returns>
    Task<MatchLineup> UpsertLineupItemAsync(MatchLineup lineup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the full lineup protocol for a specific match.
    /// Returns dynamic objects to include joined metadata like player names.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of dynamic objects representing the match protocol.</returns>
    Task<IEnumerable<dynamic>> GetByMatchIdAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific player from the match protocol by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the lineup entry.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>True if the item was successfully deleted.</returns>
    Task<bool> DeleteLineupItemAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs bulk copy of players from a team's tournament roster to the match lineup.
    /// </summary>
    /// <param name="matchId">Target match identifier.</param>
    /// <param name="teamId">Team identifier whose roster will be copied.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of records successfully inserted.</returns>
    Task<int> CopyFromRosterAsync(Guid matchId, Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a match lineup record by its unique identifier.
    /// </summary>
    /// <param name="id">The lineup entry unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The match lineup entity if found, otherwise null.</returns>
    Task<MatchLineup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single match lineup entry with extended details (player names, position name) using a dynamic result.
    /// </summary>
    /// <param name="id">The unique identifier of the lineup entry.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dynamic object containing enriched lineup details if found; otherwise, null.</returns>
    Task<dynamic?> GetMatchLineupByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
}