using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using TTA.BusinessLogic.Services;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.WebAPI.Authorization;

[assembly: InternalsVisibleTo("TTA.WebAPI.Tests")]

namespace TTA.WebAPI.Middleware;

/// <summary>
/// Middleware that automatically ensures the authenticated Auth0 user exists in the local database (JIT provisioning).
/// </summary>
/// <param name="next">The delegate representing the next middleware in the HTTP request pipeline.</param>
/// <param name="cache">The memory cache used to prevent unnecessary database queries for active user sessions.</param>
public class UserSynchronizationMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(10);
    internal static readonly ConcurrentDictionary<string, KeyedLock> UserLocks = new();

    private const int MinDisplayNameLength = 3;
    private const int MaxDisplayNameLength = 50;
    private const string DefaultFallbackDisplayName = "User";

    /// <summary>
    /// Wrapper for user-keyed semaphore and active reference count to allow safe dynamic cleanup.
    /// </summary>
    internal class KeyedLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
    }

    /// <summary>
    /// Executes the middleware to inspect the authenticated user's claims and synchronize their profile with the database on cache miss.
    /// Performs validation and fallback logic for display name to satisfy database length constraints.
    /// Deduplicates concurrent in-flight synchronization requests for the same user with reference-safe lock cleanup.
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

            var userId = FirstNonBlank(
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                user.FindFirst(IdentityResourceClaimsTypes.Sub)?.Value);

            if (!string.IsNullOrEmpty(userId))
            {
                var cacheKey = $"jit_user_{userId}";

                if (!cache.TryGetValue(cacheKey, out _))
                {
                    KeyedLock lockEntry;
                    lock (UserLocks)
                    {
                        lockEntry = UserLocks.GetOrAdd(userId, _ => new KeyedLock());
                        lockEntry.RefCount++;
                    }

                    var lockAcquired = false;
                    try
                    {
                        await lockEntry.Semaphore.WaitAsync(context.RequestAborted);
                        lockAcquired = true;

                        // Double-checked locking to avoid redundant DB upserts during concurrent request bursts
                        if (!cache.TryGetValue(cacheKey, out _))
                        {
                            var email = FirstNonBlank(
                                user.FindFirst($"{ns}{IdentityResourceClaimsTypes.Email}")?.Value,
                                user.FindFirst(ClaimTypes.Email)?.Value) ?? string.Empty;

                            var rawDisplayName = FirstNonBlank(
                                user.FindFirst($"{ns}display_name")?.Value,
                                user.FindFirst(ClaimTypes.Name)?.Value);

                            var displayName = ResolveValidDisplayName(rawDisplayName, email, userId);

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
                    finally
                    {
                        if (lockAcquired)
                        {
                            lockEntry.Semaphore.Release();
                        }

                        lock (UserLocks)
                        {
                            lockEntry.RefCount--;
                            if (lockEntry.RefCount == 0)
                            {
                                UserLocks.TryRemove(userId, out _);
                            }
                        }
                    }
                }
            }
        }

        await next(context);
    }

    /// <summary>
    /// Returns the first non-null and non-whitespace string from the provided candidates.
    /// </summary>
    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    /// <summary>
    /// Resolves a display name satisfying database constraints (length between 3 and 50 characters).
    /// Cascades through rawDisplayName, email, userId, and default fallback string using LINQ filtering.
    /// </summary>
    private static string ResolveValidDisplayName(string? rawDisplayName, string email, string userId)
    {
        var candidates = new[] { rawDisplayName, email, userId, DefaultFallbackDisplayName };

        var selected = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!.Trim())
            .FirstOrDefault(candidate => candidate.Length >= MinDisplayNameLength) ?? DefaultFallbackDisplayName;

        return selected.Length > MaxDisplayNameLength
            ? selected[..MaxDisplayNameLength]
            : selected;
    }
}