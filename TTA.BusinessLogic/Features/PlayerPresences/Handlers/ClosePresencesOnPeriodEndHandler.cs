using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.PlayerPresences.Notifications;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.PlayerPresences.Handlers;

/// <summary>
/// Event handler that responds to the <see cref="PeriodEndedNotification"/>.
/// Automatically sets the TimeOut column for all active players on the field when a period ends.
/// </summary>
/// <param name="playerPresenceRepository">The specific domain data engine repository for writing field logs.</param>
/// <param name="logger">The application-scoped diagnostic system component used for debugging context.</param>
public class ClosePresencesOnPeriodEndHandler(
    IPlayerPresenceRepository playerPresenceRepository,
    ILogger<ClosePresencesOnPeriodEndHandler> logger) : INotificationHandler<PeriodEndedNotification>
{
    private readonly IPlayerPresenceRepository _playerPresenceRepository = playerPresenceRepository;
    private readonly ILogger<ClosePresencesOnPeriodEndHandler> _logger = logger;

    /// <summary>
    /// Executes the bulk update to close player sessions for the finished period.
    /// </summary>
    /// <param name="notification">The received target system domain notification record wrapper object instance.</param>
    /// <param name="cancellationToken">A token structure configured for watching cancellation pipelines indicators.</param>
    /// <returns>An asynchronous task operation reference handler tracking completion pipelines.</returns>
    public async Task Handle(PeriodEndedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received PeriodEndedNotification for Match {MatchId}, Period {Period}. Closing active player presences.",
            notification.MatchId, notification.PeriodNumber);

        await _playerPresenceRepository.CloseActivePresencesAsync(
            notification.MatchId,
            notification.PeriodNumber,
            notification.EndTime,
            cancellationToken);

        _logger.LogInformation("Successfully closed all active player presences for Match {MatchId}, Period {Period}.",
            notification.MatchId, notification.PeriodNumber);
    }
}