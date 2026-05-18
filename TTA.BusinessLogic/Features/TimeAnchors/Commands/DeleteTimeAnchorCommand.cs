using MediatR;

namespace TTA.BusinessLogic.Features.TimeAnchors.Commands;

/// <summary>
/// Command to permanently remove a time anchor record from the match timeline.
/// </summary>
/// <param name="MatchId">The unique identifier of the associated match (used for authorization scope validation).</param>
/// <param name="Id">The unique identifier of the time anchor to delete.</param>
public record DeleteTimeAnchorCommand(Guid MatchId, Guid Id) : IRequest<bool>;