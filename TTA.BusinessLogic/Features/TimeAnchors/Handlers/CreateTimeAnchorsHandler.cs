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
/// Handles the atomic persistence of a batch of time anchors with state sequence validation.
/// </summary>
/// <param name="timeAnchorRepository">The repository for time anchor data operations.</param>
/// <param name="matchRepository">The repository for validating match existence.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class CreateTimeAnchorsHandler(
    ITimeAnchorRepository timeAnchorRepository,
    IMatchRepository matchRepository,
    ILogger<CreateTimeAnchorsHandler> logger) : IRequestHandler<CreateTimeAnchorsCommand, IEnumerable<Guid>>
{
    private readonly ITimeAnchorRepository _timeAnchorRepository = timeAnchorRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<CreateTimeAnchorsHandler> _logger = logger;

    /// <summary>
    /// Validates match existence, sequence order, and persists a batch of time anchors.
    /// </summary>
    /// <param name="request">The command containing batch anchor details and match context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of identifiers for the created time anchors.</returns>
    /// <exception cref="NotFoundException">Thrown when the specified match does not exist.</exception>
    /// <exception cref="ConflictException">Thrown when state machine transition or parameter rules fail.</exception>
    public async Task<IEnumerable<Guid>> Handle(CreateTimeAnchorsCommand request, CancellationToken cancellationToken)
    {
        var anchorReqs = request.Anchors.ToList();
        _logger.LogInformation("Attempting to create batch of {Count} TimeAnchors for Match {MatchId}.",
            anchorReqs.Count, request.MatchId);

        _ = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException($"Match with ID {request.MatchId} was not found.");

        var existingAnchors = (await _timeAnchorRepository.GetMatchAnchorsAsync(request.MatchId, cancellationToken)).ToList();
        var candidateModels = anchorReqs.ToModel(request.MatchId);

        foreach (var candidate in candidateModels)
        {
            var existingAnchor = existingAnchors.FirstOrDefault(a => a.Id == candidate.Id);
            if (existingAnchor != null &&
                (existingAnchor.Type != candidate.Type ||
                 existingAnchor.PeriodNumber != candidate.PeriodNumber ||
                 existingAnchor.Timestamp != candidate.Timestamp))
            {
                throw new ConflictException($"Time anchor with ID {candidate.Id} already exists with different parameters.");
            }
        }

        var candidateIds = candidateModels.Select(c => c.Id).ToHashSet();
        var mergedPeriodAnchors = existingAnchors
            .Where(a => !candidateIds.Contains(a.Id))
            .Concat(candidateModels)
            .OrderBy(a => a.Timestamp)
            .ThenBy(a => a.Id)
            .ToList();

        ValidateSequence(mergedPeriodAnchors);

        try
        {
            var results = await _timeAnchorRepository.UpsertAsync(candidateModels, cancellationToken);
            return results.Select(r => r.Id).ToList();
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Time anchor batch creation failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }

    /// <summary>
    /// Validates that the full chronological sequence of anchors follows state machine transition rules.
    /// </summary>
    /// <param name="anchors">The complete list of period anchors, ordered by timestamp.</param>
    private static void ValidateSequence(List<TimeAnchor> anchors)
    {
        var periodGroups = anchors.GroupBy(a => a.PeriodNumber);
        foreach (var group in periodGroups)
        {
            var state = new PeriodState();
            foreach (var anchor in group)
            {
                state.ApplyTransition(anchor);
            }
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