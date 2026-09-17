namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for user event definition presets.
/// </summary>
public interface IUserEventPresetRepository
{
    /// <summary>
    /// Persists the active user event preset layout and ordering for a specific sport.
    /// </summary>
    /// <param name="userId">The unique identifier of the target user.</param>
    /// <param name="sportId">The unique identifier of the target sport.</param>
    /// <param name="eventDefinitionIds">The ordered collection of enabled event definition identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SavePresetAsync(string userId, Guid sportId, IEnumerable<Guid> eventDefinitionIds, CancellationToken cancellationToken = default);
}
