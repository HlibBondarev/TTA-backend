namespace TTA.BusinessLogic.Features.PlayerPresences.DTOs;

/// <summary>
/// Data transfer object representing a single player's presence timeline segment during a match.
/// </summary>
/// <param name="Id">The unique identifier of the presence record.</param>
/// <param name="MatchLineupId">The unique identifier of the player's lineup protocol entry.</param>
/// <param name="PeriodNumber">The match period sequence number during which the presence occurred.</param>
/// <param name="TimeIn">The exact UTC timestamp when the player entered the field.</param>
/// <param name="TimeOut">The exact UTC timestamp when the player left the field (null if currently active).</param>
public record PlayerPresenceResponse(
    Guid Id,
    Guid MatchLineupId,
    int PeriodNumber,
    DateTime TimeIn,
    DateTime? TimeOut);