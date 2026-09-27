using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the deletion of a specific player entry from the match lineup.
/// Validates existence and ensures data integrity regarding linked game events.
/// </summary>
public class DeletePlayerFromMatchLineupHandler(
    IMatchLineupRepository matchLineupRepository,
    ILogger<DeletePlayerFromMatchLineupHandler> logger) : IRequestHandler<DeletePlayerFromMatchLineupCommand, bool>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<DeletePlayerFromMatchLineupHandler> _logger = logger;

    /// <summary>
    /// Processes the command to delete a player from the match lineup.
    /// </summary>
    /// <param name="request">The command containing the ID of the lineup entry to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the deletion was successful.</returns>
    /// <exception cref="NotFoundException">Thrown when the lineup entry does not exist.</exception>
    /// <exception cref="ConflictException">Thrown when the entry cannot be deleted due to linked game events.</exception>
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

        // 2. Data Integrity Check: Prevent deletion if player has linked match events (goals, cards, etc.)
        await EnsureNoLinkedEvents(request.Id, cancellationToken);

        // 3. Execute deletion via repository
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

    /// <summary>
    /// Ensures that the lineup entry is not referenced by any game events.
    /// This validates the business rule and prevents database constraint violations.
    /// </summary>
    /// <param name="lineupId">The ID of the match lineup entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ConflictException">Thrown when linked events are detected.</exception>
    private async Task EnsureNoLinkedEvents(Guid lineupId, CancellationToken ct)
    {
        var hasEvents = await _matchLineupRepository.HasLinkedEventsAsync(lineupId, ct);

        if (hasEvents)
        {
            _logger.LogWarning("Deletion blocked: Match lineup item {Id} has associated game events.", lineupId);
            throw new ConflictException("This player cannot be removed from the lineup because there are game events (e.g., goals or cards) linked to them.");
        }
    }
}