using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

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
    /// Retrieves the complete lineup protocol for all teams participating in a match.
    /// Returns strongly typed projections with joined metadata.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of lineup projections for the entire match context.</returns>
    Task<IEnumerable<MatchLineupProjection>> GetMatchLineupsAsync(
        Guid matchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the lineup protocol for a specific team in a match.
    /// Returns strongly typed projections with joined metadata.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of lineup projections for the specified team.</returns>
    Task<IEnumerable<MatchLineupProjection>> GetTeamMatchLineupAsync(
        Guid matchId,
        Guid teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific player from the match protocol by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the lineup entry.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>True if the item was successfully deleted.</returns>
    Task<bool> DeleteLineupItemAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies selected players from the team's tournament roster to the specific match lineup.
    /// </summary>
    /// <param name="matchId">The unique identifier of the target match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="playerRosterIds">The list of player roster identifiers to be copied into the match protocol.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The number of players successfully added to the match lineup.</returns>
    Task<int> CopyFromRosterAsync(
        Guid matchId,
        Guid teamId,
        IEnumerable<Guid> playerRosterIds,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Verifies if there are any game events (goals, cards, etc.) linked to a specific match lineup item.
    /// </summary>
    /// <param name="id">The unique identifier of the match lineup entry.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains true if linked events exist; otherwise, false.</returns>
    Task<bool> HasLinkedEventsAsync(Guid id, CancellationToken cancellationToken = default);
}