using TTA.BusinessLogic.Features.Matches.Commands;

namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Data transfer object for creating a new match.
/// </summary>
/// <param name="HomeTeamId">The unique identifier of the home team.</param>
/// <param name="GuestTeamId">The unique identifier of the guest team.</param>
/// <param name="ScheduledAt">The scheduled date and time of the match.</param>
/// <param name="MatchNumber">The optional match number.</param>
/// <param name="Venue">The optional venue of the match.</param>
public record ScheduleMatchRequest(
    Guid HomeTeamId,
    Guid GuestTeamId,
    DateTime ScheduledAt,
    string? MatchNumber,
    string? Venue);

/// <summary>
/// Mapping extensions for <see cref="ScheduleMatchRequest"/>.
/// </summary>
public static class ScheduleMatchRequestExtensions
{
    public static ScheduleMatchCommand ToCommand(this ScheduleMatchRequest request, Guid tournamentId) => new(
         TournamentId: tournamentId,
         HomeTeamId: request.HomeTeamId,
         GuestTeamId: request.GuestTeamId,
         ScheduledAt: request.ScheduledAt,
         MatchNumber: request.MatchNumber,
         Venue: request.Venue);
}
