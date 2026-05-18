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
        _logger.LogInformation("Attempting to create TimeAnchor {Type} for Match {MatchId}, Period {Period}.",
            request.Type, request.MatchId, request.PeriodNumber);

        // 1. Basic existence check
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null)
        {
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

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
            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            throw new ConflictException(ex.MessageText, ex);
        }
    }

    /// <summary>
    /// Validates that the new anchor follows the logical rules of the match state.
    /// </summary>
    private static void ValidateSequence(CreateTimeAnchorCommand request, List<TimeAnchor> existing)
    {
        // Pre-compute states to reduce cognitive complexity
        var last = existing.LastOrDefault();
        bool hasPeriodStarted = existing.Any(a => a.Type == TimeAnchorType.PeriodStart);
        bool hasPeriodEnded = existing.Any(a => a.Type == TimeAnchorType.PeriodEnd);
        bool isStoppageActive = last?.Type == TimeAnchorType.StoppageStart;

        switch (request.Type)
        {
            case TimeAnchorType.PeriodStart:
                if (hasPeriodStarted)
                    throw new ConflictException($"Period {request.PeriodNumber} already started.");
                break;

            case TimeAnchorType.PeriodEnd:
                if (!hasPeriodStarted)
                    throw new ConflictException($"Cannot end period {request.PeriodNumber} before it starts.");
                if (hasPeriodEnded)
                    throw new ConflictException($"Period {request.PeriodNumber} is already finished.");
                if (isStoppageActive)
                    throw new ConflictException("Cannot end period: a stoppage is currently active.");
                break;

            case TimeAnchorType.StoppageStart:
                if (!hasPeriodStarted || hasPeriodEnded)
                    throw new ConflictException("Stoppage can only occur during an active period.");
                if (isStoppageActive)
                    throw new ConflictException("Match is already stopped.");
                break;

            case TimeAnchorType.StoppageEnd:
                if (!isStoppageActive)
                    throw new ConflictException("Cannot end stoppage: Match was not stopped.");
                break;
        }
    }
}