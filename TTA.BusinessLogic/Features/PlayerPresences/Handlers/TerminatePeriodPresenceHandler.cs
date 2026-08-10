using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.PlayerPresences.Handlers;

/// <summary>
/// Handles the explicit termination of active player presences for a specific match period and lineup selection.
/// </summary>
/// <param name="playerPresenceRepository">The repository for player presence data operations.</param>
/// <param name="matchRepository">The repository for validating match existence.</param>
/// <param name="matchLineupRepository">The repository for validating match lineup associations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class TerminatePeriodPresenceHandler(
    IPlayerPresenceRepository playerPresenceRepository,
    IMatchRepository matchRepository,
    IMatchLineupRepository matchLineupRepository,
    ILogger<TerminatePeriodPresenceHandler> logger) : IRequestHandler<TerminatePeriodPresenceCommand>
{
    private readonly IPlayerPresenceRepository _playerPresenceRepository = playerPresenceRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<TerminatePeriodPresenceHandler> _logger = logger;

    /// <summary>
    /// Validates match existence, verifies lineup ownership, and bulk updates active presence records with the provided TimeOut timestamp.
    /// </summary>
    /// <param name="request">The command containing match, period, lineup scope, and time parameters.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="NotFoundException">Thrown when the specified match or any player lineup ID does not exist for the match.</exception>
    /// <exception cref="ConflictException">Thrown when a database constraint or business rule is violated.</exception>
    public async Task Handle(TerminatePeriodPresenceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to terminate active player presences for Match {MatchId}, Period {Period}, Lineups count: {Count}.",
            request.MatchId, request.PeriodNumber, request.PlayerLineupIds.Count());

        // 1. Validate match existence
        _ = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException($"Match with ID {request.MatchId} was not found.");

        // 2. Validate that all requested player lineup IDs belong to the target match
        var matchLineups = await _matchLineupRepository.GetMatchLineupsAsync(request.MatchId, cancellationToken);
        var validLineupIds = matchLineups.Select(l => l.Id).ToHashSet();

        var invalidLineupIds = request.PlayerLineupIds.Where(id => !validLineupIds.Contains(id)).ToList();
        if (invalidLineupIds.Count > 0)
        {
            _logger.LogWarning("Termination request for Match {MatchId} contains invalid or cross-match lineup IDs: {InvalidIds}",
                request.MatchId, string.Join(',', invalidLineupIds));
            throw new NotFoundException($"One or more specified player lineup IDs do not belong to Match {request.MatchId}.");
        }

        // 3. Execute bulk update to set TimeOut for active presences matching target lineups
        try
        {
            await _playerPresenceRepository.CloseActivePresencesAsync(
                request.MatchId,
                request.PeriodNumber,
                request.TimeOut,
                request.PlayerLineupIds,
                cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001") // Custom PL/pgSQL exception for business rules
        {
            _logger.LogWarning(ex, "Period presence termination failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }

        _logger.LogInformation("Successfully terminated active player presences for Match {MatchId}, Period {Period}.",
            request.MatchId, request.PeriodNumber);
    }
}