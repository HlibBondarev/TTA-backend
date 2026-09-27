using MediatR;

namespace TTA.BusinessLogic.Features.GameEvents.Commands;

/// <summary>
/// Immutable command record used to trigger the batch piecewise-linear time normalization 
/// for all game events associated with a specific match and team context.
/// </summary>
/// <param name="MatchId">The unique identifier of the match whose events require normalization.</param>
/// <param name="TeamId">The unique identifier of the team whose specific events will be updated.</param>
public record NormalizeMatchTimeCommand(Guid MatchId, Guid TeamId) : IRequest;