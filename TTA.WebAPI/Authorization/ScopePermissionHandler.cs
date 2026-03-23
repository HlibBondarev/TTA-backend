using Microsoft.AspNetCore.Authorization;
using Microsoft.Net.Http.Headers;
using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;

namespace TTA.WebAPI.Authorization;

/// <summary>
/// Handles <see cref="ScopePermissionRequirement"/> by fetching user profile from identity provider
/// and validating effective permissions against the requested resource scope.
/// </summary>
public class ScopePermissionHandler(
    IAccessService accessService,
    ICurrentUserService currentUserService,
    ILogger<ScopePermissionHandler> logger)
    : AuthorizationHandler<ScopePermissionRequirement>
{
    /// <summary>
    /// Evaluates the requirement by retrieving user claims via <see cref="ICurrentUserService"/>
    /// and checking access via <see cref="IAccessService"/>.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <param name="requirement">The requirement to evaluate.</param>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopePermissionRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        if (httpContext == null) return;

        // 1. Extract Authorization header to propagate it to the Identity Provider
        if (!httpContext.Request.Headers.TryGetValue(HeaderNames.Authorization, out var authHeader))
        {
            logger.LogWarning("Authorization denied: Missing Authorization header in request.");
            return;
        }

        try
        {
            // 2. Fetch user properties (including Sub/UserId) from the identity provider
            var userFromClaims = await currentUserService.GetUserPropertiesFromClaims(authHeader.ToString(), httpContext.RequestAborted);
            var userId = userFromClaims.Id; // Assuming Id property contains the Auth0 'sub'

            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("Authorization denied: Identity provider returned empty User ID.");
                return;
            }

            // 3. Extract Resource ID from Route Data
            string? resourceIdStr = requirement.TargetType switch
            {
                TargetScope.Club => httpContext.Request.RouteValues["clubId"]?.ToString(),
                TargetScope.Team => httpContext.Request.RouteValues["teamId"]?.ToString(),
                _ => null
            };

            Guid? resourceId = null;
            if (!string.IsNullOrEmpty(resourceIdStr) && Guid.TryParse(resourceIdStr, out var parsedId))
            {
                resourceId = parsedId;
            }

            // 4. Validate access via Business Logic service
            var hasAccess = await accessService.HasAccessAsync(
                userId,
                requirement.RequiredRole,
                requirement.TargetType,
                resourceId);

            if (hasAccess)
            {
                logger.LogDebug("Access granted for user {UserId} to {TargetType} {ResourceId}.", userId, requirement.TargetType, resourceId);
                context.Succeed(requirement);
            }
            else
            {
                logger.LogInformation("Access denied for user {UserId} to {TargetType} {ResourceId}. Insufficient permissions.", userId, requirement.TargetType, resourceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during authorization process for user.");
            // We don't call context.Fail() to allow potential alternative handlers to run
        }
    }
}