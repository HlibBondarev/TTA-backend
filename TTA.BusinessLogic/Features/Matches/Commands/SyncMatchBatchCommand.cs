using MediatR;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Command to execute batch synchronization of match timeline entities.
/// </summary>
/// <param name="MatchId">The unique identifier of the target match context.</param>
/// <param name="Request">The batch request containing events, anchors, and presences.</param>
public record SyncMatchBatchCommand(
    Guid MatchId,
    MatchSyncBatchRequest Request) : IRequest<MatchSyncBatchResponse>;

/// <summary>
/// Extension methods for mapping DTOs to domain models during batch sync.
/// </summary>
public static class SyncMatchBatchCommandExtensions
{
    /// <summary>
    /// Maps a <see cref="CreateGameEventRequest"/> to a <see cref="GameEvent"/> domain model with UTC normalization.
    /// </summary>
    public static GameEvent ToModel(this CreateGameEventRequest req) => new()
    {
        Id = req.Id,
        MatchLineupId = req.MatchLineupId,
        EventDefinitionId = req.EventDefinitionId,
        PeriodNumber = req.PeriodNumber,
        EventTimestamp = req.EventTimestamp.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(req.EventTimestamp, DateTimeKind.Utc),
            _ => req.EventTimestamp.ToUniversalTime()
        },
        IsLeadToGoal = req.IsLeadToGoal,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a <see cref="CreateTimeAnchorRequest"/> to a <see cref="TimeAnchor"/> domain model with UTC normalization.
    /// </summary>
    public static TimeAnchor ToModel(this CreateTimeAnchorRequest req, Guid matchId) => new()
    {
        Id = req.Id,
        MatchId = matchId,
        PeriodNumber = req.PeriodNumber,
        Type = req.Type,
        Timestamp = req.Timestamp.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(req.Timestamp, DateTimeKind.Utc),
            _ => req.Timestamp.ToUniversalTime()
        }
    };

    /// <summary>
    /// Maps a <see cref="CreatePlayerPresenceRequest"/> to a <see cref="PlayerPresence"/> domain model with UTC normalization.
    /// </summary>
    public static PlayerPresence ToModel(this CreatePlayerPresenceRequest req) => new()
    {
        Id = req.Id,
        MatchLineupId = req.MatchLineupId,
        PeriodNumber = req.PeriodNumber,
        TimeIn = req.TimeIn.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(req.TimeIn, DateTimeKind.Utc),
            _ => req.TimeIn.ToUniversalTime()
        },
        TimeOut = req.TimeOut.HasValue
            ? req.TimeOut.Value.Kind switch
            {
                DateTimeKind.Unspecified => DateTime.SpecifyKind(req.TimeOut.Value, DateTimeKind.Utc),
                _ => req.TimeOut.Value.ToUniversalTime()
            }
            : null
    };
}