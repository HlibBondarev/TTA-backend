namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Data transfer object representing a match with detailed information, including team and tournament names.
/// Used for displaying match information in lists or detailed views.
/// </summary>
/// <param name="Id">The unique identifier of the match.</param>
/// <param name="TournamentId">The unique identifier of the tournament.</param>
/// <param name="TournamentName">The display name of the tournament.</param>
/// <param name="HomeTeamId">The unique identifier of the home team.</param>
/// <param name="HomeTeamName">The display name of the home team.</param>
/// <param name="GuestTeamId">The unique identifier of the guest team.</param>
/// <param name="GuestTeamName">The display name of the guest team.</param>
/// <param name="ScheduledAt">The date and time when the match is scheduled to occur.</param>
/// <param name="MatchNumber">The optional administrative or sequence number assigned to the match.</param>
/// <param name="Venue">The location or stadium where the match is played.</param>
/// <param name="Temperature">The recorded ambient temperature during the match (optional).</param>
/// <param name="HomeScore">The number of goals/points scored by the home team (optional).</param>
/// <param name="GuestScore">The number of goals/points scored by the guest team (optional).</param>
/// <param name="CreatedAt">The timestamp when the match record was initially created.</param>
/// <param name="TrackedTeamId">The unique identifier of the specific team tracked by the user for this match (optional).</param>
public record MatchWithDetailsResponse(
    Guid Id,
    Guid TournamentId,
    string TournamentName,
    Guid HomeTeamId,
    string HomeTeamName,
    Guid GuestTeamId,
    string GuestTeamName,
    DateTime ScheduledAt,
    string? MatchNumber,
    string? Venue,
    double? Temperature,
    int? HomeScore,
    int? GuestScore,
    DateTime CreatedAt,
    Guid? TrackedTeamId = null);