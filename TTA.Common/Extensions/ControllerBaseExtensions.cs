using Microsoft.AspNetCore.Mvc;
using System.Security.Authentication;
using TTA.Common.Services;
using TTA.Common.Services.Api;
using TTA.Common.Services.DTOs;

namespace TTA.Common.Extensions;

/// <summary>
/// Provides extension methods for <see cref="ControllerBase"/> to manage user claims and identification.
/// </summary>
public static class ControllerBaseExtensions
{
    /// <summary>
    /// Retrieves the unique identifier (Sub) of the current user.
    /// </summary>
    /// <param name="controllerBase">The controller instance.</param>
    /// <param name="currentUserService">The service used to retrieve user properties from claims.</param>
    /// <returns>A string representing the user's unique identifier.</returns>
    /// <exception cref="AuthenticationException">Thrown when the 'Sub' claim is missing from the context.</exception>
    public static async Task<string> GetUserId(this ControllerBase controllerBase, ICurrentUserService currentUserService)
    {
        var userFromClaims = await GetUserClaims(controllerBase, currentUserService);

        return userFromClaims.Id ?? throw new AuthenticationException(
            $"Can not get user's claim {nameof(IdentityResourceClaimsTypes.Sub)} from Context.");
    }

    /// <summary>
    /// Retrieves the full user claims data from the authorization header.
    /// </summary>
    /// <param name="controllerBase">The controller instance.</param>
    /// <param name="currentUserService">The service used to retrieve user properties from claims.</param>
    /// <returns>A <see cref="UserFromClaimsDto"/> containing the user's information.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the Authorization header is missing from the request.</exception>
    /// <exception cref="AuthenticationException">Thrown when user claims cannot be retrieved from the context.</exception>
    public static async Task<UserFromClaimsDto> GetUserClaims(this ControllerBase controllerBase, ICurrentUserService currentUserService)
    {
        var authorizationHeader = controllerBase.Request.Headers["Authorization"];
        var token = authorizationHeader.FirstOrDefault() ??
            throw new InvalidOperationException("The request headers don't have the Authorization header.");

        var userFromClaims = (await currentUserService.GetUserPropertiesFromClaims(token)) ??
            throw new AuthenticationException("Can not get user's claims from Context.");

        return userFromClaims;
    }
}
