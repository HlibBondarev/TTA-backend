namespace TTA.BusinessLogic.Features.GameEvents.DTOs;

/// <summary>
/// Represents the unified data transfer object for initiating batch event time normalization.
/// Used by both team editors and tournament organizers to pass route identifiers.
/// </summary>
/// <param name="MatchId">The unique identifier of the target match.</param>
/// <param name="TeamId">The unique identifier of the target team.</param>
public record NormalizeMatchTimeRequest(Guid MatchId, Guid TeamId);