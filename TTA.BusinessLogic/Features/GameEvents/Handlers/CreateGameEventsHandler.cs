using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.GameEvents.Handlers;

/// <summary>
/// Handles the atomic persistence of a batch of game events with cross-entity validation and logging.
/// </summary>
/// <param name="gameEventRepository">The repository for game event data operations.</param>
/// <param name="matchLineupRepository">The repository for validating match lineup entries.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class CreateGameEventsHandler(
    IGameEventRepository gameEventRepository,
    IMatchLineupRepository matchLineupRepository,
    ILogger<CreateGameEventsHandler> logger) : IRequestHandler<CreateGameEventsCommand, IEnumerable<Guid>>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<CreateGameEventsHandler> _logger = logger;

    /// <summary>
    /// Validates match lineups and persists a batch of game events.
    /// </summary>
    /// <param name="request">The command containing batch event details and match context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of identifiers for the created game events.</returns>
    /// <exception cref="NotFoundException">Thrown when a specified match lineup entry is not found.</exception>
    /// <exception cref="ConflictException">Thrown when a player is not registered in the match or DB rules fail.</exception>
    public async Task<IEnumerable<Guid>> Handle(CreateGameEventsCommand request, CancellationToken cancellationToken)
    {
        var eventsList = request.Events.ToList();
        _logger.LogInformation("Attempting to create batch of {Count} game events for Match {MatchId}.",
            eventsList.Count, request.MatchId);

        foreach (var lineupId in eventsList.Select(evt => evt.MatchLineupId))
        {
            var lineupEntry = await _matchLineupRepository.GetByIdAsync(lineupId, cancellationToken);
            if (lineupEntry == null)
            {
                _logger.LogWarning("Game event creation failed: MatchLineup {MatchLineupId} not found.", lineupId);
                throw new NotFoundException($"The specified player protocol entry {lineupId} was not found.");
            }

            if (lineupEntry.MatchId != request.MatchId)
            {
                _logger.LogWarning("Game event creation failed: Player {MatchLineupId} does not belong to Match {MatchId}.",
                    lineupId, request.MatchId);
                throw new ConflictException("The specified player is not registered in this match's protocol.");
            }
        }

        var models = eventsList.ToModel();

        try
        {
            var results = await _gameEventRepository.UpsertAsync(models, cancellationToken);
            var resultIds = results.Select(r => r.Id).ToList();

            _logger.LogInformation("Batch of {Count} game events successfully created for Match {MatchId}.", resultIds.Count, request.MatchId);
            return resultIds;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _logger.LogWarning(ex, "Batch game event creation failed: Reference violation for Match {MatchId}.", request.MatchId);
            throw new ConflictException("Related record (Event Definition or Match Lineup) no longer exists.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Batch game event creation failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }
}