using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.EventDefinitions.Handlers;

/// <summary>
/// Handles the persistence of user event definition presets and layout ordering.
/// </summary>
/// <param name="userEventPresetRepository">The repository for user event presets data access.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
public class SaveUserEventPresetHandler(
    IUserEventPresetRepository userEventPresetRepository,
    ILogger<SaveUserEventPresetHandler> logger)
    : IRequestHandler<SaveUserEventPresetCommand>
{
    private readonly IUserEventPresetRepository _userEventPresetRepository = userEventPresetRepository;
    private readonly ILogger<SaveUserEventPresetHandler> _logger = logger;

    /// <summary>
    /// Executes the process of persisting user event definition presets and layout order.
    /// </summary>
    /// <param name="request">The command containing user ID, sport ID, and ordered definition IDs.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Handle(
        SaveUserEventPresetCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving event definition preset for User {UserId} and Sport {SportId}.", request.UserId, request.SportId);

        await _userEventPresetRepository.SavePresetAsync(
            request.UserId,
            request.SportId,
            request.EventDefinitionIds,
            cancellationToken);

        _logger.LogInformation("Successfully saved event definition preset for User {UserId} and Sport {SportId}.", request.UserId, request.SportId);
    }
}