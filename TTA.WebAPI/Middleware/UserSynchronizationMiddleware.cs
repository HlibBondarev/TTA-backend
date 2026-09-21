using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using TTA.BusinessLogic.Services;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.WebAPI.Authorization;

namespace TTA.WebAPI.Middleware;

/// <summary>
/// Middleware that automatically ensures the authenticated Auth0 user exists in the local database (JIT provisioning).
/// </summary>
/// <param name="next">The delegate representing the next middleware in the HTTP request pipeline.</param>
/// <param name="cache">The memory cache used to prevent unnecessary database queries for active user sessions.</param>
public class UserSynchronizationMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Executes the middleware to inspect the authenticated user's claims and synchronize their profile with the database on cache miss.
    /// Performs validation and fallback logic for display name to satisfy database length constraints.
    /// </summary>
    /// <param name="context">The current HTTP context containing request and user claims information.</param>
    /// <param name="userRepository">The repository used to perform user persistence and upsert operations.</param>
    /// <param name="auth0Settings">The configuration settings used to resolve custom claim namespaces.</param>
    /// <returns>A task that represents the completion of HTTP pipeline processing.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        IUserRepository userRepository,
        Auth0Settings auth0Settings)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var ns = auth0Settings.Namespace;

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst(IdentityResourceClaimsTypes.Sub)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                var cacheKey = $"jit_user_{userId}";

                if (!cache.TryGetValue(cacheKey, out _))
                {
                    var email = user.FindFirst($"{ns}{IdentityResourceClaimsTypes.Email}")?.Value
                        ?? user.FindFirst(ClaimTypes.Email)?.Value
                        ?? string.Empty;

                    var rawDisplayName = user.FindFirst($"{ns}display_name")?.Value
                        ?? user.FindFirst(ClaimTypes.Name)?.Value;

                    // Fallback to email if display name is missing, empty, or shorter than 3 characters (database constraint check)
                    var displayName = !string.IsNullOrWhiteSpace(rawDisplayName) && rawDisplayName.Trim().Length >= 3
                        ? rawDisplayName.Trim()
                        : email;

                    var userEntity = new User
                    {
                        Id = userId,
                        Email = email,
                        DisplayName = displayName,
                        CreatedAt = DateTime.UtcNow
                    };

                    await userRepository.UpsertAsync(userEntity, context.RequestAborted);

                    cache.Set(cacheKey, true, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = CacheSlidingExpiration
                    });
                }
            }
        }

        await next(context);
    }
}