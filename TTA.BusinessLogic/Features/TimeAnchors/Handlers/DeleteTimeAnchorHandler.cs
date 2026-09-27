using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.TimeAnchors.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.TimeAnchors.Handlers;

/// <summary>
/// Handles the deletion of a specific time anchor.
/// Ensures the record exists before invoking the repository deletion logic.
/// </summary>
/// <param name="timeAnchorRepository">The repository for time anchor data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class DeleteTimeAnchorHandler(
    ITimeAnchorRepository timeAnchorRepository,
    ILogger<DeleteTimeAnchorHandler> logger) : IRequestHandler<DeleteTimeAnchorCommand, bool>
{
    private readonly ITimeAnchorRepository _timeAnchorRepository = timeAnchorRepository;
    private readonly ILogger<DeleteTimeAnchorHandler> _logger = logger;

    /// <summary>
    /// Processes the removal of a time anchor record.
    /// </summary>
    /// <param name="request">The command containing the anchor and match identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, returning true if the deletion was successful.</returns>
    /// <exception cref="NotFoundException">Thrown when the time anchor does not exist or does not belong to the specified match.</exception>
    public async Task<bool> Handle(DeleteTimeAnchorCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to delete time anchor {Id} for match {MatchId}.", request.Id, request.MatchId);

        // 1. Verify existence and match scope before deletion to prevent IDOR vulnerabilities
        var existingAnchor = await _timeAnchorRepository.GetByIdAsync(request.Id, cancellationToken);

        if (existingAnchor == null || existingAnchor.MatchId != request.MatchId)
        {
            _logger.LogWarning("Deletion failed: Time anchor {Id} not found or does not belong to match {MatchId}.", request.Id, request.MatchId);
            throw new NotFoundException($"Time anchor with ID {request.Id} was not found for the specified match.");
        }

        // 2. Perform deletion via repository using the dedicated storage function
        var isDeleted = await _timeAnchorRepository.DeleteAsync(request.Id, cancellationToken);

        if (isDeleted)
        {
            _logger.LogInformation("Time anchor {Id} successfully deleted.", request.Id);
        }
        else
        {
            _logger.LogError("Time anchor {Id} was found but the deletion operation failed in the database.", request.Id);
        }

        return isDeleted;
    }
}