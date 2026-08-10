using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.GameEvents.Handlers;

/// <summary>
/// Handles the update of an existing game event with detailed logging and security checks.
/// </summary>
/// <param name="gameEventRepository">The repository for game event data operations.</param>
/// <param name="matchLineupRepository">The repository for validating match lineup entries.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class UpdateGameEventHandler(
    IGameEventRepository gameEventRepository,
    IMatchLineupRepository matchLineupRepository,
    ILogger<UpdateGameEventHandler> logger) : IRequestHandler<UpdateGameEventCommand, Guid>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<UpdateGameEventHandler> _logger = logger;

    /// <summary>
    /// Verifies event existence and player affiliation before updating the game event record.
    /// </summary>
    /// <param name="request">The command containing updated data and match context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the identifier of the updated event.</returns>
    /// <exception cref="NotFoundException">Thrown when the game event or match lineup is not found.</exception>
    /// <exception cref="ConflictException">Thrown when the player does not belong to the target match protocol.</exception>
    public async Task<Guid> Handle(UpdateGameEventCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to update game event {Id} for Match {MatchId}.", request.Id, request.MatchId);

        var existingEvent = await _gameEventRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingEvent == null)
        {
            _logger.LogWarning("Game event update failed: Event {Id} not found.", request.Id);
            throw new NotFoundException($"Game event with ID {request.Id} was not found.");
        }

        var lineupEntry = await _matchLineupRepository.GetByIdAsync(request.MatchLineupId, cancellationToken);

        if (lineupEntry == null)
        {
            _logger.LogWarning("Game event update failed: MatchLineup {MatchLineupId} not found.", request.MatchLineupId);
            throw new NotFoundException($"Match lineup with ID {request.MatchLineupId} was not found.");
        }

        if (lineupEntry.MatchId != request.MatchId)
        {
            _logger.LogWarning("Game event update failed: Player {MatchLineupId} is invalid for Match {MatchId}.",
                request.MatchLineupId, request.MatchId);
            throw new ConflictException("The selected player is not part of this match's protocol.");
        }

        var model = request.SetToModel(existingEvent);

        try
        {
            var result = await _gameEventRepository.UpsertAsync([model], cancellationToken);

            _logger.LogInformation("Game event {Id} successfully updated for Match {MatchId}.", result.ToList()[0].Id, request.MatchId);
            return result.ToList()[0].Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _logger.LogWarning(ex, "Game event update failed: Reference violation for Match {MatchId}.", request.MatchId);
            throw new ConflictException("Related record (Event Definition or Match Lineup) no longer exists.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Game event update failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }
}