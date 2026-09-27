using MediatR;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Command to share a tracked match with another user by their email address.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="TeamId">The unique identifier of the team.</param>
/// <param name="CurrentUserId">The identifier of the caller sharing the match.</param>
/// <param name="Email">The email address of the target user to share with.</param>
public record AddUserToTrackedMatchCommand(
    Guid MatchId,
    Guid TeamId,
    string CurrentUserId,
    string Email) : IRequest<bool>;