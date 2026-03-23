using Microsoft.AspNetCore.Mvc;
using System.Security.Authentication;
using TTA.BusinessLogic.Services;
using TTA.BusinessLogic.Services.Api;
using TTA.BusinessLogic.Services.DTOs;

namespace TTA.WebAPI.Extensions;

public static class ControllerBaseExtensions
{
    /// <summary>
    /// Retrieves the unique identifier (Sub) of the current user.
    /// </summary>
    /// <param name="controllerBase">The controller instance.</param>
    /// <param name="currentUserService">The service used to retrieve user properties from claims.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A string representing the user's unique identifier.</returns>
    /// <exception cref="AuthenticationException">Thrown when the 'sub' claim is missing from the context.</exception>
    public static async Task<string> GetUserId(
        this ControllerBase controllerBase,
        ICurrentUserService currentUserService,
        CancellationToken ct = default)
    {
        var userFromClaims = await GetUserClaims(controllerBase, currentUserService, ct);

        // Use the actual constant value to ensure the error message reflects the exact claim type "sub".
        return userFromClaims.Id ?? throw new AuthenticationException(
            $"Can not get user's claim {IdentityResourceClaimsTypes.Sub} from Context.");
    }

    /// <summary>
    /// Retrieves the full user claims data from the authorization header.
    /// </summary>
    /// <param name="controllerBase">The controller instance.</param>
    /// <param name="currentUserService">The service used to retrieve user properties from claims.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="UserFromClaimsDto"/> containing the user's information.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the Authorization header is missing or invalid.</exception>
    /// <exception cref="AuthenticationException">Thrown when user claims cannot be retrieved from the context.</exception>
    public static async Task<UserFromClaimsDto> GetUserClaims(
        this ControllerBase controllerBase,
        ICurrentUserService currentUserService,
        CancellationToken ct = default)
    {
        var authorizationHeader = controllerBase.Request.Headers.Authorization.FirstOrDefault();

        // Validate that the header exists and is not just whitespace
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            throw new InvalidOperationException("The request headers don't have a valid Authorization header.");
        }

        // Pass the CancellationToken down to the service call
        var userFromClaims = await currentUserService.GetUserPropertiesFromClaims(authorizationHeader, ct) ??
            throw new AuthenticationException("Can not get user's claims from Context.");

        return userFromClaims;
    }
}