using TTA.BusinessLogic.Features.Matches.Commands;

namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Data transfer object for adding another user to a tracked match by their email address.
/// </summary>
/// <param name="Email">The email address of the target user to share the tracked match with.</param>
public record AddUserToTrackedMatchRequest(string Email);

/// <summary>
/// Mapping extensions for <see cref="AddUserToTrackedMatchRequest"/>.
/// </summary>
public static class AddUserToTrackedMatchRequestExtensions
{
    /// <summary>
    /// Maps the request DTO to a MediatR execution command.
    /// </summary>
    /// <param name="request">The request DTO containing the target user's email.</param>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="currentUserId">The identifier of the authenticated user sharing the match.</param>
    /// <returns>A configured <see cref="AddUserToTrackedMatchCommand"/> instance.</returns>
    public static AddUserToTrackedMatchCommand ToCommand(
        this AddUserToTrackedMatchRequest request,
        Guid matchId,
        Guid teamId,
        string currentUserId) => new(
            MatchId: matchId,
            TeamId: teamId,
            CurrentUserId: currentUserId,
            Email: request.Email);
}