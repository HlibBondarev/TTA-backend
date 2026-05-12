using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.GameEvents.Handlers;

/// <summary>
/// Handles the deletion of a specific game event.
/// Validates existence before performing the data access operation.
/// </summary>
/// <param name="gameEventRepository">The repository for game event data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class DeleteGameEventHandler(
    IGameEventRepository gameEventRepository,
    ILogger<DeleteGameEventHandler> logger) : IRequestHandler<DeleteGameEventCommand, bool>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly ILogger<DeleteGameEventHandler> _logger = logger;

    /// <summary>
    /// Processes the removal of a game event record.
    /// </summary>
    /// <param name="request">The command containing the event identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, returning true if the deletion was successful.</returns>
    /// <exception cref="NotFoundException">Thrown when the game event with the specified ID does not exist.</exception>
    public async Task<bool> Handle(DeleteGameEventCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to delete game event {Id}.", request.Id);

        // 1. Verify existence before deletion
        var existingEvent = await _gameEventRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingEvent == null)
        {
            _logger.LogWarning("Deletion failed: Game event {Id} not found.", request.Id);
            throw new NotFoundException($"Game event with ID {request.Id} was not found.");
        }

        // 2. Perform deletion via repository
        // The repository's DeleteAsync uses SqlStatements.ForGameEvents.DeleteEvent internally.
        var isDeleted = await _gameEventRepository.DeleteAsync(request.Id, cancellationToken);

        if (isDeleted)
        {
            _logger.LogInformation("Game event {Id} successfully deleted.", request.Id);
        }
        else
        {
            // This case might occur if the database function returns false or 0 rows affected unexpectedly
            _logger.LogError("Game event {Id} was found but the deletion operation failed in the database.", request.Id);
        }

        return isDeleted;
    }
}