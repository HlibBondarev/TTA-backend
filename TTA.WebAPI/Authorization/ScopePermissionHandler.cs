using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;

namespace TTA.WebAPI.Authorization;

/// <summary>
/// Authorizes access by evaluating <see cref="ScopePermissionRequirement"/> against 
/// the authenticated user's claims and database-stored access policies.
/// </summary>
/// <remarks>
/// This handler extracts the user's unique identifier (sub) directly from the 
/// <see cref="ClaimsPrincipal"/> to avoid redundant round-trips to the identity provider.
/// </remarks>
public class ScopePermissionHandler(
    IAccessService accessService,
    ILogger<ScopePermissionHandler> logger)
    : AuthorizationHandler<ScopePermissionRequirement>
{
    /// <summary>
    /// Evaluates the requirement by extracting the user ID from claims and 
    /// validating permissions via <see cref="IAccessService"/>.
    /// </summary>
    /// <param name="context">The authorization context containing the user and resource.</param>
    /// <param name="requirement">The specific permission requirement to evaluate.</param>
    /// <returns>A task representing the asynchronous evaluation process.</returns>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopePermissionRequirement requirement)
    {
        // 1. Get HttpContext from the resource
        if (context.Resource is not HttpContext httpContext)
        {
            return;
        }

        try
        {
            // 2. Extract 'sub' (Subject) directly from the already authenticated User Principal.
            // This is the unique User ID from Auth0/Identity Provider.
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("Authorization denied: 'sub' or 'NameIdentifier' claim is missing.");
                return;
            }

            // 3. Extract Resource ID from Route Data based on the target scope
            var resourceId = GetResourceIdFromRoute(httpContext, requirement.TargetType);

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
                // We don't call context.Fail() to allow other handlers to potentially succeed
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while processing authorization for a user.");
        }
    }

    /// <summary>
    /// Helper method to extract the specific resource ID (Club or Team) from the current route.
    /// </summary>
    private static Guid? GetResourceIdFromRoute(HttpContext httpContext, TargetScope targetType)
    {
        string? resourceIdStr = targetType switch
        {
            TargetScope.Club => httpContext.Request.RouteValues["clubId"]?.ToString(),
            TargetScope.Team => httpContext.Request.RouteValues["teamId"]?.ToString(),
            _ => null
        };

        return Guid.TryParse(resourceIdStr, out var parsedId) ? parsedId : null;
    }
}