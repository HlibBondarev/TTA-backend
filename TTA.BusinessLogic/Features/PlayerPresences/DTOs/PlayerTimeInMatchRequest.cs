namespace TTA.BusinessLogic.Features.PlayerPresences.DTOs;

/// <summary>
/// Request parameters model used for API parameter binding to calculate player time-in-match performance analytics.
/// </summary>
/// <param name="MatchId">The unique database reference key for the target match.</param>
/// <param name="TeamId">The unique reference key for the target team.</param>
public record PlayerTimeInMatchRequest(
    Guid MatchId,
    Guid TeamId
);