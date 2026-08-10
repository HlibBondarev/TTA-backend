using TTA.BusinessLogic.Features.PlayerPresences.Commands;

namespace TTA.BusinessLogic.Features.PlayerPresences.DTOs;

/// <summary>
/// Data transfer object representing an incoming HTTP request to explicitly close active player presences for a finished match period.
/// </summary>
/// <param name="PeriodNumber">The specific match period sequence number that has ended.</param>
/// <param name="PlayerLineupIds">The explicit collection of match lineup identifiers whose presence sessions are being closed.</param>
/// <param name="TimeOut">The client-side UTC timestamp marking when play ended.</param>
public record TerminatePresenceRequest(
    int PeriodNumber,
    IEnumerable<Guid> PlayerLineupIds,
    DateTime TimeOut);

/// <summary>
/// Provides mapping extension methods for transforming termination requests into domain commands.
/// </summary>
public static class TerminatePresenceRequestExtensions
{
    /// <summary>
    /// Maps the HTTP request payload and route parameters to the internal MediatR termination command.
    /// </summary>
    /// <param name="request">The incoming data transfer object payload.</param>
    /// <param name="matchId">The targeted match unique identifier extracted from the URL route.</param>
    /// <returns>A fully populated domain command ready for MediatR execution.</returns>
    public static TerminatePeriodPresenceCommand ToCommand(this TerminatePresenceRequest request, Guid matchId)
    {
        return new TerminatePeriodPresenceCommand(
            MatchId: matchId,
            PeriodNumber: request.PeriodNumber,
            PlayerLineupIds: request.PlayerLineupIds,
            TimeOut: request.TimeOut
        );
    }
}