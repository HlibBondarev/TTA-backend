using MediatR;
using TTA.BusinessLogic.Features.GameEvents.DTOs;

namespace TTA.BusinessLogic.Features.GameEvents.Queries;

/// <summary>
/// Query to retrieve a chronological timeline of all events for a specific match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
public record GetMatchEventsTimelineQuery(Guid MatchId) : IRequest<IEnumerable<GameEventResponse>>;