using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;

namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Data transfer object representing a consolidated batch synchronization request payload.
/// </summary>
/// <param name="Events">Collection of game event requests recorded offline.</param>
/// <param name="Anchors">Collection of time anchor requests recorded offline.</param>
/// <param name="Presences">Collection of player presence interval requests recorded offline.</param>
public record MatchSyncBatchRequest(
    IEnumerable<CreateGameEventRequest> Events,
    IEnumerable<CreateTimeAnchorRequest> Anchors,
    IEnumerable<CreatePlayerPresenceRequest> Presences);