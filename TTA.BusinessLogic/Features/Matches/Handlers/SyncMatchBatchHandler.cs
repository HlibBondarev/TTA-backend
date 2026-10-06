using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the atomic persistence of a batch of match timeline entities.
/// </summary>
/// <param name="matchRepository">The repository for match data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class SyncMatchBatchHandler(
    IMatchRepository matchRepository,
    ILogger<SyncMatchBatchHandler> logger) : IRequestHandler<SyncMatchBatchCommand, MatchSyncBatchResponse>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<SyncMatchBatchHandler> _logger = logger;

    /// <summary>
    /// Validates match existence, maps DTO requests to domain models, and executes batch database synchronization.
    /// </summary>
    /// <param name="request">The command containing batch requests and match context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A response containing collections of confirmed entity identifiers.</returns>
    /// <exception cref="NotFoundException">Thrown when the specified match is not found.</exception>
    /// <exception cref="ConflictException">Thrown when database constraints or business rules fail.</exception>
    public async Task<MatchSyncBatchResponse> Handle(SyncMatchBatchCommand request, CancellationToken cancellationToken)
    {
        var matchId = request.MatchId;
        _logger.LogInformation("Initiating batch synchronization for Match {MatchId}.", matchId);

        var match = await _matchRepository.GetByIdAsync(matchId, cancellationToken);
        if (match == null)
        {
            _logger.LogWarning("Batch sync failed: Match {MatchId} not found.", matchId);
            throw new NotFoundException($"Match with ID {matchId} was not found.");
        }

        var events = request.Request.Events.Select(e => e.ToModel());
        var anchors = request.Request.Anchors.Select(a => a.ToModel(matchId));
        var presences = request.Request.Presences.Select(p => p.ToModel());

        try
        {
            var projection = await _matchRepository.SyncMatchBatchAsync(
                matchId,
                events,
                anchors,
                presences,
                cancellationToken);

            _logger.LogInformation(
                "Successfully processed batch sync for Match {MatchId}. Synced: {EventCount} events, {AnchorCount} anchors, {PresenceCount} presences.",
                matchId,
                projection.SyncedEventIds.Count(),
                projection.SyncedAnchorIds.Count(),
                projection.SyncedPresenceIds.Count());

            return new MatchSyncBatchResponse(
                MatchId: matchId,
                SyncedEventIds: projection.SyncedEventIds,
                SyncedAnchorIds: projection.SyncedAnchorIds,
                SyncedPresenceIds: projection.SyncedPresenceIds);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _logger.LogWarning(ex, "Batch sync failed: Foreign key reference violation for Match {MatchId}.", matchId);
            throw new ConflictException("One or more referenced records (Match Lineup, Event Definition, or Match) no longer exist.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            _logger.LogWarning(ex, "Batch sync failed: Check constraint violation for Match {MatchId}.", matchId);
            throw new ConflictException("One or more entities violate database constraints (e.g., TimeOut earlier than TimeIn).", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Batch sync failed due to database business rule for Match {MatchId}: {Message}", matchId, ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }
}