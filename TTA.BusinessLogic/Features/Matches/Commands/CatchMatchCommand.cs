using MediatR;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Command to link an authenticated user to a specific match and team context for tracking.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="TeamId">The unique identifier of the team.</param>
/// <param name="UserId">The identifier of the user catching the match.</param>
public record CatchMatchCommand(
    Guid MatchId,
    Guid TeamId,
    string UserId) : IRequest<bool>;