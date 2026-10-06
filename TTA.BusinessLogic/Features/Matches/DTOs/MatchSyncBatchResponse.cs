namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Data transfer object containing the arrays of successfully synchronized entity identifiers.
/// </summary>
/// <param name="MatchId">The unique identifier of the synchronized match context.</param>
/// <param name="SyncedEventIds">Collection of confirmed game event identifiers.</param>
/// <param name="SyncedAnchorIds">Collection of confirmed time anchor identifiers.</param>
/// <param name="SyncedPresenceIds">Collection of confirmed player presence identifiers.</param>
public record MatchSyncBatchResponse(
    Guid MatchId,
    IEnumerable<Guid> SyncedEventIds,
    IEnumerable<Guid> SyncedAnchorIds,
    IEnumerable<Guid> SyncedPresenceIds);