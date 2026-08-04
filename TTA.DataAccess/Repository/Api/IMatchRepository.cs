using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the Match entity.
/// </summary>
public interface IMatchRepository : IEntityRepositoryBase<Guid, Match>
{
    /// <summary>
    /// Persists a match entity to the database using an upsert operation.
    /// </summary>
    /// <param name="match">The match entity to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted match entity.</returns>
    Task<Match> UpsertMatchAsync(Match match, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a match by its unique identifier.
    /// </summary>
    /// <param name="matchId">The match unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The match entity if found.</returns>
    Task<Match?> GetByIdAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all matches belonging to a specific tournament.
    /// Returns dynamic objects to include joined metadata like team names.
    /// </summary>
    Task<IEnumerable<dynamic>> GetByTournamentIdAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single match with extended details (like team and tournament names) using a dynamic result.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match to retrieve.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dynamic object containing all match details if found; otherwise, null.</returns>
    Task<dynamic?> GetMatchByIdWithDetailsAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions JIT quick match infrastructure (teams, tournament, rosters) and creates a match record.
    /// </summary>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="userId">The requesting user identifier for tournament ownership.</param>
    /// <param name="configurationId">The optional unique identifier of the sport configuration.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The created <see cref="Match"/> entity if successful; otherwise, null.</returns>
    Task<Match?> CreateQuickMatchAsync(
        Guid sportId,
        string userId,
        Guid? configurationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a match record by its unique identifier.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task returning true if the record was successfully deleted; otherwise, false.</returns>
    Task<bool> DeleteAsync(Guid matchId, CancellationToken cancellationToken = default);
}
