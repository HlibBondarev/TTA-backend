using MediatR;

namespace TTA.BusinessLogic.Features.PlayerPresences.Notifications;

/// <summary>
/// Domain notification triggered immediately after a match period has been officially ended via a TimeAnchor.
/// </summary>
/// <param name="MatchId">The unique database reference key for the designated match context.</param>
/// <param name="PeriodNumber">The matching numerical index sequence of the completed sports period.</param>
/// <param name="EndTime">The absolute coordinated timestamp metric defining the close of play.</param>
public record PeriodEndedNotification(Guid MatchId, int PeriodNumber, DateTime EndTime) : INotification;