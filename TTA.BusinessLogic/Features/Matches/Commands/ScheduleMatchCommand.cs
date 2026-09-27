using MediatR;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Command to create a match.
/// Returns the persisted <see cref="Match"/> entity Id.
/// </summary>
/// <param name="TournamentId">The unique identifier of the tournament associated with this match.</param>
/// <param name="HomeTeamId">The unique identifier of the home team.</param>
/// <param name="GuestTeamId">The unique identifier of the guest team.</param>
/// <param name="ScheduledAt">The scheduled date and time of the match.</param>
/// <param name="MatchNumber">The optional match number.</param>
/// <param name="Venue">The optional venue of the match.</param>
public record ScheduleMatchCommand(
    Guid TournamentId,
    Guid HomeTeamId,
    Guid GuestTeamId,
    DateTime ScheduledAt,
    string? MatchNumber,
    string? Venue) : IRequest<Guid>;

/// <summary>
/// Mapping extensions for <see cref="ScheduleMatchCommand"/>.
/// </summary>
public static class ScheduleMatchCommandExtensions
{
    /// <summary>
    /// Maps the command to a <see cref="Match"/> domain model.
    /// </summary>
    public static Match ToModel(this ScheduleMatchCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = cmd.TournamentId,
        HomeTeamId = cmd.HomeTeamId,
        GuestTeamId = cmd.GuestTeamId,
        ScheduledAt = cmd.ScheduledAt,
        MatchNumber = cmd.MatchNumber,
        Venue = cmd.Venue,
        CreatedAt = DateTime.UtcNow
    };
}