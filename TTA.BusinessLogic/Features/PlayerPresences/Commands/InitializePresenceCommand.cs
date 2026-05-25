using MediatR;

namespace TTA.BusinessLogic.Features.PlayerPresences.Commands;

/// <summary>
/// Command to initialize player presence for an explicit list of players starting a specific match period.
/// </summary>
/// <param name="MatchId">The unique identifier value of the active match context.</param>
/// <param name="PeriodNumber">The relative match period sequence number being started.</param>
/// <param name="PlayerLineupIds">The distinct collection dataset containing player lineup protocol identifiers starting this period block.</param>
public record InitializePresenceCommand(
    Guid MatchId,
    int PeriodNumber,
    IEnumerable<Guid> PlayerLineupIds) : IRequest;