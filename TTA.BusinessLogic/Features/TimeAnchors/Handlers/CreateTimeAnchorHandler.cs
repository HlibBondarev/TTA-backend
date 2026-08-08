using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.TimeAnchors.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.TimeAnchors.Handlers;

/// <summary>
/// Handles the creation of a new time anchor with match validation.
/// Relies on GlobalExceptionHandler for unhandled exceptions.
/// </summary>
/// <param name="timeAnchorRepository">The repository for time anchor data operations.</param>
/// <param name="matchRepository">The repository for validating match existence.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class CreateTimeAnchorHandler(
    ITimeAnchorRepository timeAnchorRepository,
    IMatchRepository matchRepository,
    ILogger<CreateTimeAnchorHandler> logger) : IRequestHandler<CreateTimeAnchorCommand, Guid>
{
    private readonly ITimeAnchorRepository _timeAnchorRepository = timeAnchorRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<CreateTimeAnchorHandler> _logger = logger;

    /// <summary>
    /// Validates the match existence and persists the new time anchor record.
    /// </summary>
    /// <param name="request">The command containing anchor details and match context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The unique identifier of the persisted time anchor.</returns>
    /// <exception cref="NotFoundException">Thrown when the specified match does not exist.</exception>
    /// <exception cref="ConflictException">Thrown when a database constraint or business rule is violated.</exception>
    public async Task<Guid> Handle(CreateTimeAnchorCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to create TimeAnchor {Id} ({Type}) for Match {MatchId}, Period {Period}.",
            request.Id, request.Type, request.MatchId, request.PeriodNumber);

        // 1. Basic existence check
        _ = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException($"Match with ID {request.MatchId} was not found.");

        // 2. Logical sequence and idempotency validation
        var existingAnchors = await _timeAnchorRepository.GetMatchAnchorsAsync(request.MatchId, cancellationToken);
        var existingAnchor = existingAnchors.FirstOrDefault(a => a.Id == request.Id);
        var normalizedModel = request.ToModel();

        if (existingAnchor != null &&
            (existingAnchor.Type != request.Type ||
             existingAnchor.PeriodNumber != request.PeriodNumber ||
             existingAnchor.Timestamp != normalizedModel.Timestamp))
        {
            throw new ConflictException($"Time anchor with ID {request.Id} already exists with different parameters.");
        }

        // Include candidate anchor, exclude any existing record with same ID, and order chronologically
        var periodAnchors = existingAnchors
            .Where(a => a.PeriodNumber == request.PeriodNumber && a.Id != request.Id)
            .Append(normalizedModel)
            .OrderBy(a => a.Timestamp)
            .ToList();

        ValidateSequence(periodAnchors);

        // 3. Persistence
        try
        {
            var result = await _timeAnchorRepository.UpsertAsync(normalizedModel, cancellationToken);
            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001") // Custom PL/pgSQL exception for business rules
        {
            _logger.LogWarning(ex, "Time anchor creation failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }

    /// <summary>
    /// Validates that the full chronological sequence of anchors within the period follows state machine transition rules.
    /// </summary>
    /// <param name="anchors">The complete list of period anchors including candidate anchor, ordered by timestamp.</param>
    private static void ValidateSequence(List<TimeAnchor> anchors)
    {
        var state = new PeriodState();
        foreach (var anchor in anchors)
        {
            state.ApplyTransition(anchor);
        }
    }

    /// <summary>
    /// Encapsulates the period state and validates transitions for incoming time anchors.
    /// </summary>
    private sealed class PeriodState
    {
        public bool IsStarted { get; private set; }
        public bool IsEnded { get; private set; }
        public bool IsStoppageActive { get; private set; }

        public void ApplyTransition(TimeAnchor anchor)
        {
            switch (anchor.Type)
            {
                case TimeAnchorType.PeriodStart:
                    ApplyPeriodStart(anchor.PeriodNumber);
                    break;
                case TimeAnchorType.PeriodEnd:
                    ApplyPeriodEnd(anchor.PeriodNumber);
                    break;
                case TimeAnchorType.StoppageStart:
                    ApplyStoppageStart();
                    break;
                case TimeAnchorType.StoppageEnd:
                    ApplyStoppageEnd();
                    break;
            }
        }

        private void ApplyPeriodStart(int periodNumber)
        {
            if (IsStarted)
                throw new ConflictException($"Period {periodNumber} already started.");
            IsStarted = true;
        }

        private void ApplyPeriodEnd(int periodNumber)
        {
            if (!IsStarted)
                throw new ConflictException($"Cannot end period {periodNumber} before it starts.");
            if (IsEnded)
                throw new ConflictException($"Period {periodNumber} is already finished.");
            if (IsStoppageActive)
                throw new ConflictException("Cannot end period: a stoppage is currently active.");
            IsEnded = true;
        }

        private void ApplyStoppageStart()
        {
            if (!IsStarted || IsEnded)
                throw new ConflictException("Stoppage can only occur during an active period.");
            if (IsStoppageActive)
                throw new ConflictException("Match is already stopped.");
            IsStoppageActive = true;
        }

        private void ApplyStoppageEnd()
        {
            if (!IsStoppageActive)
                throw new ConflictException("Cannot end stoppage: Match was not stopped.");
            IsStoppageActive = false;
        }
    }
}