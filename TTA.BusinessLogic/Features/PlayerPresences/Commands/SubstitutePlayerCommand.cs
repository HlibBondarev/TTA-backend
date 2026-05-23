using MediatR;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.PlayerPresences.Commands;

/// <summary>
/// Command to execute a player substitution during a match.
/// Records the exit time of the outgoing player and the entry time of the incoming player.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="PeriodNumber">The current period number.</param>
/// <param name="PlayerOutLineupId">The lineup identifier of the player leaving the field.</param>
/// <param name="PlayerInLineupId">The lineup identifier of the player entering the field.</param>
public record SubstitutePlayerCommand(
    Guid MatchId,
    int PeriodNumber,
    Guid PlayerOutLineupId,
    Guid PlayerInLineupId) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping SubstitutePlayerCommand to domain models.
/// </summary>
public static class SubstitutePlayerCommandExtensions
{
    /// <summary>
    /// Maps the substitution command to a new PlayerPresence entity for the incoming player.
    /// Sets the exact server-generated UTC timestamp for TimeIn.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <param name="substitutionTime">The exact UTC time when the substitution occurred.</param>
    /// <returns>A new PlayerPresence entity for the incoming player.</returns>
    public static PlayerPresence ToModel(this SubstitutePlayerCommand cmd, DateTime substitutionTime) => new()
    {
        Id = Guid.NewGuid(),
        MatchLineupId = cmd.PlayerInLineupId,
        PeriodNumber = cmd.PeriodNumber,
        TimeIn = substitutionTime,
        TimeOut = null
    };
}