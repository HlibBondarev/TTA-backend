using TTA.BusinessLogic.Features.Matches.Commands;

namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Represents the HTTP request payload for launching a quick match creation process.
/// </summary>
/// <param name="Id">Gets the client-generated unique identifier of the match.</param>
/// <param name="SportId">Gets the unique identifier of the target sport discipline.</param>
/// <param name="ConfigurationId">Gets the optional unique identifier of the sport configuration. If omitted, the system automatically falls back to the default configuration of the specified sport.</param>
/// <param name="TrackedTeamId">Gets the optional unique identifier of the team to track (catch) automatically upon match creation.</param>
public record CreateQuickMatchRequest(
    Guid Id,
    Guid SportId,
    Guid? ConfigurationId = null,
    Guid? TrackedTeamId = null);

/// <summary>
/// Mapping extensions for <see cref="CreateQuickMatchRequest"/>.
/// </summary>
public static class CreateQuickMatchRequestExtensions
{
    /// <summary>
    /// Maps a <see cref="CreateQuickMatchRequest"/> instance to a <see cref="CreateQuickMatchCommand"/>.
    /// </summary>
    /// <param name="request">The quick match request DTO.</param>
    /// <param name="userId">The unique identifier of the authenticated user.</param>
    /// <param name="userEmail">The email address of the authenticated user.</param>
    /// <param name="userName">The display name of the authenticated user.</param>
    /// <returns>A mapped <see cref="CreateQuickMatchCommand"/> instance.</returns>
    public static CreateQuickMatchCommand ToCommand(
        this CreateQuickMatchRequest request,
        string userId,
        string userEmail,
        string userName) => new(
            request,
            userId,
            userEmail,
            userName);
}