using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using System.Security.Claims;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Middleware;

namespace TTA.WebAPI.Tests.Middleware;

/// <summary>
/// Unit tests for verifying the authorization, caching, and claim resolution behavior of <see cref="UserSynchronizationMiddleware"/>.
/// </summary>
public class UserSynchronizationMiddlewareTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<RequestDelegate> _nextMock = new();
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly Auth0Settings _auth0Settings = new("https://auth.example.com/", "client_id", "audience", "https://tta.com/");

    /// <summary>
    /// Helper method to create an instance of <see cref="UserSynchronizationMiddleware"/> with test dependencies.
    /// </summary>
    /// <returns>A configured <see cref="UserSynchronizationMiddleware"/> instance.</returns>
    private UserSynchronizationMiddleware CreateMiddleware()
    {
        return new UserSynchronizationMiddleware(_nextMock.Object, _memoryCache);
    }

    /// <summary>
    /// Verifies that unauthenticated requests bypass database synchronization completely and proceed to the next middleware.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenUnauthenticated_ShouldSkipSynchronizationAndCallNext()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _nextMock.Verify(n => n(context), Times.Once);
    }

    /// <summary>
    /// Verifies that authenticated requests without an active cache entry execute <see cref="IUserRepository.UpsertAsync"/> and populate the memory cache.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenAuthenticatedAndNotInCache_ShouldCallUpsertAndSetCache()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", "auth0|123456"),
            new Claim("https://tta.com/email", "test@example.com"),
            new Claim("https://tta.com/display_name", "Test User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<User>(u => u.Id == "auth0|123456" && u.Email == "test@example.com" && u.DisplayName == "Test User"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _memoryCache.TryGetValue("jit_user_auth0|123456", out bool cacheExists).Should().BeTrue();
        cacheExists.Should().BeTrue();
        _nextMock.Verify(n => n(context), Times.Once);
    }

    /// <summary>
    /// Verifies that authenticated requests with an active cache entry skip database operations and invoke the next middleware directly.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenAuthenticatedAndInCache_ShouldSkipUpsertAndCallNext()
    {
        // Arrange
        var userId = "auth0|654321";
        _memoryCache.Set($"jit_user_{userId}", true);

        var context = new DefaultHttpContext();
        var claims = new[] { new Claim("sub", userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _nextMock.Verify(n => n(context), Times.Once);
    }

    /// <summary>
    /// Verifies that claim extraction correctly falls back to standard .NET claim types when custom Auth0 namespace claims are absent.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ShouldFallbackToStandardClaims_WhenCustomClaimsMissing()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "auth0|999999"),
            new Claim(ClaimTypes.Email, "standard@example.com"),
            new Claim(ClaimTypes.Name, "Standard User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<User>(u => u.Id == "auth0|999999" && u.Email == "standard@example.com" && u.DisplayName == "Standard User"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when display name and email are invalid or under 3 characters, the middleware falls back to userId or default string to satisfy database constraints.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ShouldFallbackToUserIdOrDefault_WhenDisplayNameAndEmailAreTooShort()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", "auth0|777777"),
            new Claim(ClaimTypes.Email, "a"), // Invalid length (< 3)
            new Claim(ClaimTypes.Name, "ab")  // Invalid length (< 3)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<User>(u => u.Id == "auth0|777777" && u.DisplayName == "auth0|777777"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that blank claims (empty string or whitespace) correctly fall back to subsequent non-blank claims.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ShouldSkipBlankClaims_AndUseFirstNonBlankValue()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("https://tta.com/email", "   "), // Blank custom claim
            new Claim(ClaimTypes.Email, "valid@example.com"),
            new Claim("https://tta.com/display_name", ""), // Blank custom claim
            new Claim(ClaimTypes.Name, "Valid Name"),
            new Claim(ClaimTypes.NameIdentifier, "  "), // Blank claim
            new Claim("sub", "auth0|888888")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<User>(u => u.Id == "auth0|888888" && u.Email == "valid@example.com" && u.DisplayName == "Valid Name"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that display names with surrogate pairs (emojis) are correctly counted by Unicode scalar values (runes) and not split across surrogate boundaries.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ShouldHandleUnicodeSurrogatePairsAndEmojisCorrectly()
    {
        // Arrange
        var context = new DefaultHttpContext();
        // "😀😁😂" contains 3 runes (Unicode scalar values), which satisfy MinDisplayNameLength = 3
        var claims = new[]
        {
            new Claim("sub", "auth0|unicode_123"),
            new Claim(ClaimTypes.Email, "unicode@example.com"),
            new Claim(ClaimTypes.Name, "😀😁😂")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<User>(u => u.DisplayName == "😀😁😂"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that concurrent in-flight requests for the same user only trigger a single database upsert.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ShouldDeduplicateConcurrentRequests_ForSameUser()
    {
        // Arrange
        var userId = "auth0|concurrent_123";
        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim(ClaimTypes.Email, "concurrent@example.com"),
            new Claim(ClaimTypes.Name, "Concurrent User")
        };

        var upsertCompletion = new TaskCompletionSource<(User User, bool IsInserted)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _userRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(upsertCompletion.Task);

        var middleware = CreateMiddleware();

        // Act - Spawn 5 concurrent requests from the same user on cold cache
        var tasks = Enumerable.Range(0, 5).Select(_ =>
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            };
            return middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);
        }).ToArray();

        // Verify that exactly 1 database upsert was triggered while in-flight requests are blocked
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(It.Is<User>(u => u.Id == userId), It.IsAny<CancellationToken>()),
            Times.Once);

        // Unblock upsert and wait for all concurrent requests to complete
        upsertCompletion.SetResult((new User { Id = userId }, true));
        await Task.WhenAll(tasks);

        // Assert
        _userRepositoryMock.Verify(
            r => r.UpsertAsync(It.Is<User>(u => u.Id == userId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that user lock entries are safely cleaned up from the lock dictionary once all requests complete.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ShouldCleanupUserLockEntry_WhenRequestCompletes()
    {
        // Arrange
        var userId = "auth0|cleanup_test_123";
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim(ClaimTypes.Email, "cleanup@example.com"),
            new Claim(ClaimTypes.Name, "Cleanup User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = CreateMiddleware();

        // Act
        await middleware.InvokeAsync(context, _userRepositoryMock.Object, _auth0Settings);

        // Assert
        UserSynchronizationMiddleware.UserLocks.ContainsKey(userId).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that when a waiting request is canceled via CancellationToken, the lock's reference count is still decremented and the dictionary entry cleaned up.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ShouldCleanupUserLockEntry_WhenWaitingRequestIsCancelled()
    {
        // Arrange
        var userId = "auth0|cancel_test_123";
        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim(ClaimTypes.Email, "cancel@example.com"),
            new Claim(ClaimTypes.Name, "Cancel User")
        };

        var tcs = new TaskCompletionSource<(User User, bool IsInserted)>();
        _userRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var middleware = CreateMiddleware();

        // Active request holding the lock
        var activeContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        var activeTask = middleware.InvokeAsync(activeContext, _userRepositoryMock.Object, _auth0Settings);

        // Waiting request that gets cancelled
        using var cts = new CancellationTokenSource();
        var waitingContext = new DefaultHttpContext
        {
            RequestAborted = cts.Token,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        var waitingTask = middleware.InvokeAsync(waitingContext, _userRepositoryMock.Object, _auth0Settings);

        // Cancel waiting request while blocked on WaitAsync
        cts.Cancel();

        // Waiting task should throw OperationCanceledException
        var act = async () => await waitingTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Unblock and complete the active request
        tcs.SetResult((new User { Id = userId }, true));
        await activeTask;

        // Assert
        UserSynchronizationMiddleware.UserLocks.ContainsKey(userId).Should().BeFalse();
    }
}