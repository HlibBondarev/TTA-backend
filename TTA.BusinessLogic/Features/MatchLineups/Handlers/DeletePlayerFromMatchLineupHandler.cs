using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the deletion of a match lineup entry.
/// </summary>
public class DeletePlayerFromMatchLineupHandler(
    IMatchLineupRepository matchLineupRepository,
    ILogger<DeletePlayerFromMatchLineupHandler> logger) : IRequestHandler<DeletePlayerFromMatchLineupCommand, bool>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<DeletePlayerFromMatchLineupHandler> _logger = logger;

    /// <summary>
    /// Processes the deletion command.
    /// Checks for existence before attempting to delete to provide accurate feedback.
    /// </summary>
    /// <param name="request">The command containing the ID of the record to be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the record was successfully deleted.</returns>
    /// <exception cref="NotFoundException">Thrown when the record does not exist in the database.</exception>
    public async Task<bool> Handle(DeletePlayerFromMatchLineupCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to delete match lineup entry with ID {Id}.", request.Id);

        // 1. Check if the record exists first to provide a clear NotFoundException
        var exists = await _matchLineupRepository.GetByIdAsync(request.Id, cancellationToken);
        if (exists == null)
        {
            _logger.LogWarning("Delete failed: Match lineup entry {Id} not found.", request.Id);
            throw new NotFoundException($"Match lineup entry with ID {request.Id} was not found.");
        }

        // 2. Execute deletion via repository
        // Note: The database function public.delete_match_lineup_item returns a boolean indicating success.
        var isDeleted = await _matchLineupRepository.DeleteLineupItemAsync(request.Id, cancellationToken);

        if (isDeleted)
        {
            _logger.LogInformation("Match lineup entry {Id} successfully deleted.", request.Id);
        }
        else
        {
            _logger.LogError("Match lineup entry {Id} was found but could not be deleted.", request.Id);
        }

        return isDeleted;
    }
}