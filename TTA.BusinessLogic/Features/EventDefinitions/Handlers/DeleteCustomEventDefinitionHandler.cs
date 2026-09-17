using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.EventDefinitions.Handlers;

/// <summary>
/// Handles the soft-deletion of custom user-owned event definitions.
/// </summary>
/// <param name="eventDefinitionRepository">The repository for event definition data access.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
public class DeleteCustomEventDefinitionHandler(
    IEventDefinitionRepository eventDefinitionRepository,
    ILogger<DeleteCustomEventDefinitionHandler> logger)
    : IRequestHandler<DeleteCustomEventDefinitionCommand, bool>
{
    private readonly IEventDefinitionRepository _eventDefinitionRepository = eventDefinitionRepository;
    private readonly ILogger<DeleteCustomEventDefinitionHandler> _logger = logger;

    /// <summary>
    /// Soft-deletes a custom event definition owned by a specific user.
    /// </summary>
    /// <param name="request">The command containing event definition ID and owner user ID.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>True if the soft-deletion succeeded; otherwise, false.</returns>
    public async Task<bool> Handle(
        DeleteCustomEventDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Soft-deleting custom event definition {Id} by User {UserId}.", request.Id, request.UserId);

        var result = await _eventDefinitionRepository.SoftDeleteAsync(request.Id, request.UserId, cancellationToken);

        if (!result)
        {
            _logger.LogWarning("Failed to delete event definition {Id}. Either definition was not found or user is not the owner.", request.Id);
        }
        else
        {
            _logger.LogInformation("Successfully soft-deleted custom event definition {Id}.", request.Id);
        }

        return result;
    }
}