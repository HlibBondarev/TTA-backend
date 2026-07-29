using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.PlayerPresences.Handlers;

/// <summary>
/// Handles the execution of a player substitution.
/// Updates the outgoing player's TimeOut and creates a TimeIn record for the incoming player using client-provided timestamps and IDs.
/// Supports idempotent retries for offline synchronization pipelines.
/// </summary>
/// <param name="playerPresenceRepository">The repository for player presence data operations.</param>
/// <param name="matchRepository">The repository for validating match existence.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class SubstitutePlayerHandler(
    IPlayerPresenceRepository playerPresenceRepository,
    IMatchRepository matchRepository,
    ILogger<SubstitutePlayerHandler> logger) : IRequestHandler<SubstitutePlayerCommand, Guid>
{
    private readonly IPlayerPresenceRepository _playerPresenceRepository = playerPresenceRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<SubstitutePlayerHandler> _logger = logger;

    /// <summary>
    /// Validates the match and active player state, then processes the substitution.
    /// Handles duplicate request replays idempotently when matching IncomingPresenceId is present.
    /// </summary>
    /// <param name="request">The command containing substitution details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The unique identifier of the incoming player's presence record.</returns>
    /// <exception cref="NotFoundException">Thrown when the match is not found.</exception>
    /// <exception cref="ConflictException">Thrown when the outgoing player is not active, ID is reused with mismatched payload, or DB constraints fail.</exception>
    public async Task<Guid> Handle(SubstitutePlayerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing substitution for Match {MatchId}, Period {Period}. Out: {OutId}, In: {InId}",
            request.MatchId, request.PeriodNumber, request.PlayerOutLineupId, request.PlayerInLineupId);

        // 1. Basic existence check
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null)
        {
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

        // 2. Retrieve all match presences to evaluate idempotency and active status
        var allPresences = await _playerPresenceRepository.GetMatchPresenceAsync(request.MatchId, cancellationToken);

        // 3. Idempotency Check: Verify if an incoming presence with the same IncomingPresenceId already exists
        var existingPresence = allPresences.FirstOrDefault(p => p.Id == request.IncomingPresenceId);
        if (existingPresence != null)
        {
            var isIdenticalReplay = existingPresence.MatchLineupId == request.PlayerInLineupId &&
                                    existingPresence.PeriodNumber == request.PeriodNumber &&
                                    Math.Abs((existingPresence.TimeIn - request.SubstitutionTime).TotalMilliseconds) < 500;

            if (isIdenticalReplay)
            {
                _logger.LogInformation("Idempotent replay detected for presence ID {IncomingPresenceId}. Returning existing presence ID.", request.IncomingPresenceId);
                return existingPresence.Id;
            }

            _logger.LogWarning("Mismatched reuse of IncomingPresenceId {IncomingPresenceId} detected.", request.IncomingPresenceId);
            throw new ConflictException($"IncomingPresenceId '{request.IncomingPresenceId}' has already been used with different substitution parameters.");
        }

        // 4. Find the active presence record for the outgoing player
        var activeOutgoingPresence = allPresences.FirstOrDefault(p =>
            p.MatchLineupId == request.PlayerOutLineupId &&
            p.PeriodNumber == request.PeriodNumber &&
            p.TimeOut == null);

        if (activeOutgoingPresence == null)
        {
            _logger.LogWarning("PlayerOutLineupId {PlayerOutLineupId} is not currently active on the field.", request.PlayerOutLineupId);
            throw new ConflictException("The outgoing player is not currently active on the field in this period.");
        }

        try
        {
            // 5 & 6. Atomically update the outgoing player's record and insert the incoming player's record using client timestamp and ID
            activeOutgoingPresence.TimeOut = request.SubstitutionTime;
            var incomingPresence = request.ToModel();

            await _playerPresenceRepository.RecordSubstitutionAsync(activeOutgoingPresence, incomingPresence, cancellationToken);

            _logger.LogInformation("Successfully completed substitution. New presence ID: {PresenceId}", incomingPresence.Id);

            return incomingPresence.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23514") // Check constraint violation
        {
            _logger.LogWarning(ex, "Substitution failed: Database check constraint violation for Match {MatchId}.", request.MatchId);
            throw new ConflictException("Substitution time is invalid (e.g., TimeOut occurs before TimeIn).", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
        {
            _logger.LogWarning(ex, "Substitution failed: Reference violation for Match {MatchId}.", request.MatchId);
            throw new ConflictException("Related record (Match Lineup) no longer exists.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001") // Custom PL/pgSQL exception
        {
            _logger.LogWarning(ex, "Substitution failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }
}