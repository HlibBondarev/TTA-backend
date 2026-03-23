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
        if (context.Resource is not HttpContext httpContext)
        {
            return;
        }

        try
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("Authorization denied: User identifier claim is missing.");
                return;
            }

            // 1. Create a non-PII identifier for logging (last 8 characters)
            var shortUserId = userId.Length > 8 ? $"...{userId[^8..]}" : userId;

            var resourceId = GetResourceIdFromRoute(httpContext, requirement.TargetType);

            if (requirement.TargetType != TargetScope.Global && resourceId == null)
            {
                logger.LogWarning("Authorization failed: Missing or malformed ID for {TargetType} scope.", requirement.TargetType);
                context.Fail();
                return;
            }

            var hasAccess = await accessService.HasAccessAsync(
                userId,
                requirement.RequiredRole,
                requirement.TargetType,
                resourceId);

            if (hasAccess)
            {
                // 2. Use shortUserId in logs instead of raw userId
                logger.LogDebug("Access granted for user {ShortUserId} to {TargetType} {ResourceId}.",
                    shortUserId, requirement.TargetType, resourceId);
                context.Succeed(requirement);
            }
            else
            {
                // 3. Use shortUserId in logs instead of raw userId
                logger.LogInformation("Access denied for user {ShortUserId} to {TargetType} {ResourceId}. Insufficient permissions.",
                    shortUserId, requirement.TargetType, resourceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during the authorization process.");
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