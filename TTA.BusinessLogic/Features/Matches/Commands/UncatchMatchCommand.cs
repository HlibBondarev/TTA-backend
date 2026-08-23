using MediatR;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Command to remove a tracking link between a user and a match/team context.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="TeamId">The unique identifier of the team.</param>
/// <param name="UserId">The identifier of the user uncatching the match.</param>
public record UncatchMatchCommand(
    Guid MatchId,
    Guid TeamId,
    string UserId) : IRequest<bool>;