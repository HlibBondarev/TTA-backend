using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.GameEvents.Handlers;

/// <summary>
/// Handles the creation of a new game event with cross-entity validation and detailed logging.
/// </summary>
/// <param name="gameEventRepository">The repository for game event data operations.</param>
/// <param name="matchLineupRepository">The repository for validating match lineup entries.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class CreateGameEventHandler(
    IGameEventRepository gameEventRepository,
    IMatchLineupRepository matchLineupRepository,
    ILogger<CreateGameEventHandler> logger) : IRequestHandler<CreateGameEventCommand, Guid>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<CreateGameEventHandler> _logger = logger;

    /// <summary>
    /// Validates the player's match affiliation and persists the new game event record.
    /// </summary>
    /// <param name="request">The command containing event details and match context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the unique identifier of the created event.</returns>
    /// <exception cref="NotFoundException">Thrown when the specified match lineup entry is not found.</exception>
    /// <exception cref="ConflictException">Thrown when business rules or database constraints are violated.</exception>
    public async Task<Guid> Handle(CreateGameEventCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to create game event for Match {MatchId}. Player: {MatchLineupId}.",
            request.MatchId, request.MatchLineupId);

        var lineupEntry = await _matchLineupRepository.GetByIdAsync(request.MatchLineupId, cancellationToken);

        if (lineupEntry == null)
        {
            _logger.LogWarning("Game event creation failed: MatchLineup {MatchLineupId} not found.", request.MatchLineupId);
            throw new NotFoundException("The specified player protocol entry was not found.");
        }

        if (lineupEntry.MatchId != request.MatchId)
        {
            _logger.LogWarning("Game event creation failed: Player {MatchLineupId} does not belong to Match {MatchId}.",
                request.MatchLineupId, request.MatchId);
            throw new ConflictException("The specified player is not registered in this match's protocol.");
        }

        var model = request.ToModel();

        try
        {
            var result = await _gameEventRepository.UpsertAsync(model, cancellationToken);

            _logger.LogInformation("Game event {Id} successfully created for Match {MatchId}.", result.Id, request.MatchId);
            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _logger.LogWarning(ex, "Game event creation failed: Reference violation for Match {MatchId}.", request.MatchId);
            throw new ConflictException("Related record (Event Definition or Match Lineup) no longer exists.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Game event creation failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while creating game event for Match {MatchId}.", request.MatchId);
            throw;
        }
    }
}