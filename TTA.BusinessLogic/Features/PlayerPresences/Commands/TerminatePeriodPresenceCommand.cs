using MediatR;

namespace TTA.BusinessLogic.Features.PlayerPresences.Commands;

/// <summary>
/// Command to explicitly terminate active player presences for a specific set of lineup entries in a finished period.
/// </summary>
/// <param name="MatchId">The unique identifier value of the active match context.</param>
/// <param name="PeriodNumber">The relative match period sequence number being terminated.</param>
/// <param name="PlayerLineupIds">The explicit collection of match lineup identifiers to target for presence termination.</param>
/// <param name="TimeOut">The client-generated UTC timestamp marking when players left the field.</param>
public record TerminatePeriodPresenceCommand(
    Guid MatchId,
    int PeriodNumber,
    IEnumerable<Guid> PlayerLineupIds,
    DateTime TimeOut) : IRequest;