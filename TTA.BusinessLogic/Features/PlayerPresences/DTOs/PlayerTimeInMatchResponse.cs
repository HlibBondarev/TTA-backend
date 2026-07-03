namespace TTA.BusinessLogic.Features.PlayerPresences.DTOs;

/// <summary>
/// Represents the calculated performance analytics response containing clean and dirty play times for a specific match lineup record.
/// </summary>
/// <param name="MatchLineupId">The unique database reference identifier for the player's match lineup record.</param>
/// <param name="CleanTimeInMatch">The calculated clean active play time after normalization adjustments.</param>
/// <param name="DirtyTimeInMatch">The raw linear elapsed time spent in the match before adjustments.</param>
public record PlayerTimeInMatchResponse(
    Guid MatchLineupId,
    TimeSpan CleanTimeInMatch,
    TimeSpan DirtyTimeInMatch
);