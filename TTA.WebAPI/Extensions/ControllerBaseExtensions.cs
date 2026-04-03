using Microsoft.AspNetCore.Mvc;
using System.Security.Authentication;
using System.Security.Claims;
using TTA.BusinessLogic.Services;
using TTA.BusinessLogic.Services.DTOs;
using TTA.WebAPI.Authorization;

namespace TTA.WebAPI.Extensions;

public static class ControllerBaseExtensions
{
    /// <summary>
    /// Retrieves the unique identifier (Sub) of the current user.
    /// </summary>
    /// <param name="controllerBase">The controller instance.</param>
    /// <returns>A string representing the user's unique identifier.</returns>
    /// <exception cref="AuthenticationException">Thrown when the 'sub' claim is missing from the context.</exception>
    public static string GetUserId(this ControllerBase controllerBase, Auth0Settings auth0Settings)
    {
        var userFromClaims = GetUserClaims(controllerBase, auth0Settings);

        if (userFromClaims.Id == string.Empty)
        {
            // Use the actual constant value to ensure the error message reflects the exact claim type "sub".
            throw new AuthenticationException($"Can not get user's claim {IdentityResourceClaimsTypes.Sub} from Context.");
        }

        return userFromClaims.Id;
    }

    /// <summary>
    /// Retrieves the full user claims data from the authorization header.
    /// </summary>
    /// <param name="controllerBase">The controller instance.</param>
    /// <param name="currentUserService">The service used to retrieve user properties from claims.</param>
    /// <returns>A <see cref="UserFromClaimsDto"/> containing the user's information.</returns>
    public static UserFromClaimsDto GetUserClaims(
        this ControllerBase controllerBase, Auth0Settings auth0Settings)
    {
        // Use the namespace from the injected settings
        var ns = auth0Settings.Namespace;
        var user = controllerBase.User;

        return new UserFromClaimsDto
        (
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst(IdentityResourceClaimsTypes.Sub)?.Value ?? string.Empty,
            user.FindFirst($"{ns}{IdentityResourceClaimsTypes.Email}")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
            user.FindFirst($"{ns}display_name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty
        );
    }
}