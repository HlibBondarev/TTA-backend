using MediatR;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.PlayerPresences.Commands;

/// <summary>
/// Command to execute a player substitution during a match.
/// Records the exit time of the outgoing player and the entry time of the incoming player using client-provided identifiers and timestamp.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="PeriodNumber">The current period number.</param>
/// <param name="PlayerOutLineupId">The lineup identifier of the player leaving the field.</param>
/// <param name="PlayerInLineupId">The lineup identifier of the player entering the field.</param>
/// <param name="IncomingPresenceId">The client-generated unique identifier for the incoming presence entity.</param>
/// <param name="SubstitutionTime">The client-generated UTC timestamp marking when the substitution occurred.</param>
public record SubstitutePlayerCommand(
    Guid MatchId,
    int PeriodNumber,
    Guid PlayerOutLineupId,
    Guid PlayerInLineupId,
    Guid IncomingPresenceId,
    DateTime SubstitutionTime) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping SubstitutePlayerCommand to domain models.
/// </summary>
public static class SubstitutePlayerCommandExtensions
{
    /// <summary>
    /// Maps the substitution command to a new PlayerPresence entity for the incoming player using the client-supplied ID and timestamp.
    /// </summary>
    /// <param name="cmd">The command instance containing substitution parameters.</param>
    /// <returns>A new PlayerPresence entity configured for the incoming player.</returns>
    public static PlayerPresence ToModel(this SubstitutePlayerCommand cmd) => new()
    {
        Id = cmd.IncomingPresenceId,
        MatchLineupId = cmd.PlayerInLineupId,
        PeriodNumber = cmd.PeriodNumber,
        TimeIn = cmd.SubstitutionTime,
        TimeOut = null
    };
}