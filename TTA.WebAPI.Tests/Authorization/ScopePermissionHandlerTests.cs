using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Moq;
using System.Security.Claims;
using TTA.BusinessLogic.Services.Api;
using TTA.BusinessLogic.Services.DTOs;
using TTA.Common.Enums;
using TTA.WebAPI.Authorization;

namespace TTA.WebAPI.Tests.Authorization;

/// <summary>
/// Unit tests for ScopePermissionHandler to ensure proper integration between
/// HTTP context data, user identity fetching, and access validation.
/// </summary>
public class ScopePermissionHandlerTests
{
    private readonly Mock<IAccessService> _accessServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<ScopePermissionHandler>> _loggerMock;
    private readonly ScopePermissionHandler _handler;

    public ScopePermissionHandlerTests()
    {
        _accessServiceMock = new Mock<IAccessService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ScopePermissionHandler>>();

        _handler = new ScopePermissionHandler(
            _accessServiceMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldSucceed_WhenUserHasValidPermission()
    {
        // Arrange
        var userId = "auth0|789";
        var clubId = Guid.NewGuid();
        var authHeader = "Bearer valid-token";
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HeaderNames.Authorization] = authHeader;

        // FIX: Manually inject the Routing Feature
        var routeValuesFeature = new RouteValuesFeature
        {
            RouteValues = new RouteValueDictionary { { "clubId", clubId.ToString() } }
        };
        httpContext.Features.Set<IRouteValuesFeature>(routeValuesFeature);

        // FIX: Use positional constructor for the record
        var userDto = new UserFromClaimsDto(userId, "test@example.com", "Test User");

        _currentUserServiceMock
            .Setup(x => x.GetUserPropertiesFromClaims(authHeader, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDto);

        _accessServiceMock
            .Setup(x => x.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Club, clubId))
            .ReturnsAsync(true);

        var authContext = new AuthorizationHandlerContext(new[] { requirement }, new System.Security.Claims.ClaimsPrincipal(), httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }
    [Fact]
    public async Task HandleAsync_ShouldNotSucceed_WhenUserIdIsMissingFromClaims()
    {
        // Arrange
        var authHeader = "Bearer token-with-no-sub";
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HeaderNames.Authorization] = authHeader;

        _currentUserServiceMock
            .Setup(x => x.GetUserPropertiesFromClaims(authHeader, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserFromClaimsDto(string.Empty, "test@example.com", "Test User"));

        var authContext = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotSucceed_WhenAccessServiceReturnsFalse()
    {
        // Arrange
        var userId = "auth0|denied-user";
        var authHeader = "Bearer some-token";
        var requirement = new ScopePermissionRequirement(AppRole.FullControl, TargetScope.Club);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HeaderNames.Authorization] = authHeader;

        _currentUserServiceMock
            .Setup(x => x.GetUserPropertiesFromClaims(authHeader, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserFromClaimsDto(userId, "test@example.com", "Test User"));

        _accessServiceMock
            .Setup(x => x.HasAccessAsync(userId, It.IsAny<AppRole>(), It.IsAny<TargetScope>(), It.IsAny<Guid?>()))
            .ReturnsAsync(false);

        var authContext = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldNotSucceed_WhenAuthHeaderIsMissing()
    {
        // Arrange
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club);
        var httpContext = new DefaultHttpContext(); // No Authorization header added here

        var authContext = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
        // Verify that access service was never even called
        _accessServiceMock.Verify(x => x.HasAccessAsync(It.IsAny<string>(), It.IsAny<AppRole>(), It.IsAny<TargetScope>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldNotSucceed_WhenIdentityReturnsEmptyId()
    {
        // Arrange
        var authHeader = "Bearer token";
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = authHeader;

        // Identity provider returns a DTO but the Id is null/empty
        _currentUserServiceMock
            .Setup(x => x.GetUserPropertiesFromClaims(authHeader, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserFromClaimsDto(string.Empty, "test@test.com", "User"));

        var authContext = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ShouldSucceed_WhenRequirementIsGlobal()
    {
        // Arrange
        var userId = "auth0|global-user";
        var authHeader = "Bearer global-token";

        // 1. Requirement with TargetScope.Global
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Global);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HeaderNames.Authorization] = authHeader;
        // Note: No route values are set because Global scope doesn't target a specific resource ID

        _currentUserServiceMock
            .Setup(x => x.GetUserPropertiesFromClaims(authHeader, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserFromClaimsDto(userId, "admin@tta.com", "Admin User"));

        // 2. Mock AccessService to return true for Global scope (resourceId must be null)
        _accessServiceMock
            .Setup(x => x.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Global, null))
            .ReturnsAsync(true);

        var authContext = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);

        // Verify that the service was indeed called with null for the resourceId
        _accessServiceMock.Verify(x => x.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Global, null), Times.Once);
    }
}