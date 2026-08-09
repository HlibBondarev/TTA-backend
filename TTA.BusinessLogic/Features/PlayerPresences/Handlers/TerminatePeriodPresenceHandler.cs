using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.PlayerPresences.Handlers;

/// <summary>
/// Handles the explicit termination of active player presences for a specific match period and lineup selection.
/// </summary>
/// <param name="playerPresenceRepository">The repository for player presence data operations.</param>
/// <param name="matchRepository">The repository for validating match existence.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class TerminatePeriodPresenceHandler(
    IPlayerPresenceRepository playerPresenceRepository,
    IMatchRepository matchRepository,
    ILogger<TerminatePeriodPresenceHandler> logger) : IRequestHandler<TerminatePeriodPresenceCommand>
{
    private readonly IPlayerPresenceRepository _playerPresenceRepository = playerPresenceRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<TerminatePeriodPresenceHandler> _logger = logger;

    /// <summary>
    /// Validates match existence and bulk updates active presence records with the provided TimeOut timestamp.
    /// </summary>
    /// <param name="request">The command containing match, period, lineup scope, and time parameters.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="NotFoundException">Thrown when the specified match does not exist.</exception>
    public async Task Handle(TerminatePeriodPresenceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to terminate active player presences for Match {MatchId}, Period {Period}, Lineups count: {Count}.",
            request.MatchId, request.PeriodNumber, request.PlayerLineupIds.Count());

        // 1. Validate match existence
        _ = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException($"Match with ID {request.MatchId} was not found.");

        // 2. Execute bulk update to set TimeOut for active presences matching target lineups
        await _playerPresenceRepository.CloseActivePresencesAsync(
            request.MatchId,
            request.PeriodNumber,
            request.TimeOut,
            request.PlayerLineupIds,
            cancellationToken);

        _logger.LogInformation("Successfully terminated active player presences for Match {MatchId}, Period {Period}.",
            request.MatchId, request.PeriodNumber);
    }
}