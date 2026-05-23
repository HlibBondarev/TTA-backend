using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.PlayerPresences.Notifications;
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
/// <param name="mediator">The mediator instance used for publishing domain notification events.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class CreateTimeAnchorHandler(
    ITimeAnchorRepository timeAnchorRepository,
    IMatchRepository matchRepository,
    IMediator mediator,
    ILogger<CreateTimeAnchorHandler> logger) : IRequestHandler<CreateTimeAnchorCommand, Guid>
{
    private readonly ITimeAnchorRepository _timeAnchorRepository = timeAnchorRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IMediator _mediator = mediator;
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
        _logger.LogInformation("Attempting to create TimeAnchor {Type} for Match {MatchId}, Period {Period}.",
            request.Type, request.MatchId, request.PeriodNumber);

        // 1. Basic existence check
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException($"Match with ID {request.MatchId} was not found.");

        // 2. Logical sequence validation
        var existingAnchors = await _timeAnchorRepository.GetMatchAnchorsAsync(request.MatchId, cancellationToken);
        var periodAnchors = existingAnchors
            .Where(a => a.PeriodNumber == request.PeriodNumber)
            .OrderBy(a => a.Timestamp)
            .ToList();

        ValidateSequence(request, periodAnchors);

        // 3. Persistence
        var model = request.ToModel();
        try
        {
            var result = await _timeAnchorRepository.UpsertAsync(model, cancellationToken);

            // 4. Domain Trigger: Automatically close active player presence sessions if period finishes
            if (model.Type == TimeAnchorType.PeriodEnd)
            {
                await _mediator.Publish(new PeriodEndedNotification(model.MatchId, model.PeriodNumber, model.Timestamp), cancellationToken);
            }

            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            throw new ConflictException(ex.MessageText, ex);
        }
    }

    /// <summary>
    /// Validates that the new anchor follows the logical rules of the match state.
    /// Routes the validation to specific helper methods based on the anchor type.
    /// </summary>
    /// <param name="request">The command containing anchor payload data details.</param>
    /// <param name="existing">The list dataset of existing anchors within the same period scope.</param>
    private static void ValidateSequence(CreateTimeAnchorCommand request, List<TimeAnchor> existing)
    {
        bool hasPeriodStarted = existing.Any(a => a.Type == TimeAnchorType.PeriodStart);
        bool hasPeriodEnded = existing.Any(a => a.Type == TimeAnchorType.PeriodEnd);
        bool isStoppageActive = existing.LastOrDefault()?.Type == TimeAnchorType.StoppageStart;

        switch (request.Type)
        {
            case TimeAnchorType.PeriodStart:
                ValidatePeriodStart(request.PeriodNumber, hasPeriodStarted);
                break;
            case TimeAnchorType.PeriodEnd:
                ValidatePeriodEnd(request.PeriodNumber, hasPeriodStarted, hasPeriodEnded, isStoppageActive);
                break;
            case TimeAnchorType.StoppageStart:
                ValidateStoppageStart(hasPeriodStarted, hasPeriodEnded, isStoppageActive);
                break;
            case TimeAnchorType.StoppageEnd:
                ValidateStoppageEnd(isStoppageActive);
                break;
        }
    }

    /// <summary>
    /// Enforces validation constraints for a PeriodStart anchor type.
    /// </summary>
    /// <param name="periodNumber">The target period identifier number sequence context.</param>
    /// <param name="hasPeriodStarted">Indicates whether a start anchor already exists for the period layout.</param>
    private static void ValidatePeriodStart(int periodNumber, bool hasPeriodStarted)
    {
        if (hasPeriodStarted)
            throw new ConflictException($"Period {periodNumber} already started.");
    }

    /// <summary>
    /// Enforces validation constraints for a PeriodEnd anchor type.
    /// </summary>
    /// <param name="periodNumber">The target period identifier number sequence context.</param>
    /// <param name="hasPeriodStarted">Indicates whether a start anchor exists for the period layout.</param>
    /// <param name="hasPeriodEnded">Indicates whether an end anchor already exists for the period layout.</param>
    /// <param name="isStoppageActive">Indicates whether a match stoppage section remains unclosed.</param>
    private static void ValidatePeriodEnd(int periodNumber, bool hasPeriodStarted, bool hasPeriodEnded, bool isStoppageActive)
    {
        if (!hasPeriodStarted)
            throw new ConflictException($"Cannot end period {periodNumber} before it starts.");
        if (hasPeriodEnded)
            throw new ConflictException($"Period {periodNumber} is already finished.");
        if (isStoppageActive)
            throw new ConflictException("Cannot end period: a stoppage is currently active.");
    }

    /// <summary>
    /// Enforces validation constraints for a StoppageStart anchor type.
    /// </summary>
    /// <param name="hasPeriodStarted">Indicates whether a start anchor exists for the period layout.</param>
    /// <param name="hasPeriodEnded">Indicates whether an end anchor exists for the period layout.</param>
    /// <param name="isStoppageActive">Indicates whether a stoppage block is currently open.</param>
    private static void ValidateStoppageStart(bool hasPeriodStarted, bool hasPeriodEnded, bool isStoppageActive)
    {
        if (!hasPeriodStarted || hasPeriodEnded)
            throw new ConflictException("Stoppage can only occur during an active period.");
        if (isStoppageActive)
            throw new ConflictException("Match is already stopped.");
    }

    /// <summary>
    /// Enforces validation constraints for a StoppageEnd anchor type.
    /// </summary>
    /// <param name="isStoppageActive">Indicates whether a match stoppage section is open.</param>
    private static void ValidateStoppageEnd(bool isStoppageActive)
    {
        if (!isStoppageActive)
            throw new ConflictException("Cannot end stoppage: Match was not stopped.");
    }
}