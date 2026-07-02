using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the PlayerPresence entity.
/// Handles individual substitutions and bulk starting lineup initialization.
/// </summary>
public interface IPlayerPresenceRepository : IEntityRepositoryBase<Guid, PlayerPresence>
{
    /// <summary>
    /// Persists a player presence record (TimeIn) or updates it (TimeOut) using an upsert operation.
    /// </summary>
    /// <param name="entity">The player presence entity to save.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The unique identifier of the persisted record.</returns>
    Task<Guid> RecordPresenceAsync(PlayerPresence entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically records a player substitution by updating the outgoing player's session and inserting the incoming player's session within a single database transaction.
    /// </summary>
    /// <param name="outgoingPresence">The presence record of the player leaving the field.</param>
    /// <param name="incomingPresence">The presence record of the player entering the field.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The unique identifier of the newly created incoming presence record.</returns>
    Task<Guid> RecordSubstitutionAsync(PlayerPresence outgoingPresence, PlayerPresence incomingPresence, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all player presence records associated with a specific match.
    /// Results are returned in chronological order as defined by the storage function.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of player presence entities.</returns>
    Task<IEnumerable<PlayerPresence>> GetMatchPresenceAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk inserts presence records for an explicit array of player lineup IDs starting the period.
    /// </summary>
    /// <param name="periodNumber">The specific match period sequence number being initialized.</param>
    /// <param name="timeIn">The exact UTC timestamp marking when the period started and players entered the field.</param>
    /// <param name="lineupIds">The collection of unique player lineup identifiers for the active players starting this period.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests during the asynchronous operation.</param>
    Task InitializePeriodPresenceAsync(int periodNumber, DateTime timeIn, IEnumerable<Guid> lineupIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the timeout column for all active presence records within the specified match and period scope.
    /// </summary>
    /// <param name="matchId">The unique database reference key for the target match.</param>
    /// <param name="periodNumber">The specific match period number that has just concluded.</param>
    /// <param name="timeOut">The exact UTC timestamp marking when the period ended, used to close open player sessions.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests during the asynchronous operation.</param>
    Task CloseActivePresencesAsync(Guid matchId, int periodNumber, DateTime timeOut, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the raw linear ("dirty") time spent in the water per player lineup row, grouped by match periods.
    /// </summary>
    /// <param name="matchId">The unique database reference key for the target match.</param>
    /// <param name="teamId">The unique reference key for the target team.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests during the asynchronous operation.</param>
    /// <returns>A collection of typed projections containing lineup reference, period index, and total dirty seconds.</returns>
    Task<IEnumerable<PlayersDirtyTimeByPeriodProjection>> GetPlayersDirtyTimeByPeriodAsync(Guid matchId, Guid teamId, CancellationToken cancellationToken = default);
}