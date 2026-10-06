namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Projection representing confirmed synchronized entity identifiers returned from batch operations.
/// </summary>
public record MatchSyncBatchProjection
{
    /// <summary>
    /// Collection of synchronized game event identifiers.
    /// </summary>
    public IEnumerable<Guid> SyncedEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// Collection of synchronized time anchor identifiers.
    /// </summary>
    public IEnumerable<Guid> SyncedAnchorIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// Collection of synchronized player presence identifiers.
    /// </summary>
    public IEnumerable<Guid> SyncedPresenceIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// Parameterless constructor required for Dapper ORM materialization.
    /// </summary>
    public MatchSyncBatchProjection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchSyncBatchProjection"/> record.
    /// </summary>
    public MatchSyncBatchProjection(
        IEnumerable<Guid> syncedEventIds,
        IEnumerable<Guid> syncedAnchorIds,
        IEnumerable<Guid> syncedPresenceIds)
    {
        SyncedEventIds = syncedEventIds ?? Array.Empty<Guid>();
        SyncedAnchorIds = syncedAnchorIds ?? Array.Empty<Guid>();
        SyncedPresenceIds = syncedPresenceIds ?? Array.Empty<Guid>();
    }
}