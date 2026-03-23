using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;
using TTA.WebAPI.Authorization;

namespace TTA.WebAPI.Tests.Authorization;

/// <summary>
/// Unit tests for ScopePermissionHandler to ensure proper extraction of user identity 
/// from claims and validation of permissions via AccessService.
/// </summary>
public class ScopePermissionHandlerTests
{
    private readonly Mock<IAccessService> _accessServiceMock;
    private readonly Mock<ILogger<ScopePermissionHandler>> _loggerMock;
    private readonly ScopePermissionHandler _handler;

    public ScopePermissionHandlerTests()
    {
        _accessServiceMock = new Mock<IAccessService>();
        _loggerMock = new Mock<ILogger<ScopePermissionHandler>>();

        // ICurrentUserService is no longer needed in the constructor
        _handler = new ScopePermissionHandler(
            _accessServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldSucceed_WhenUserHasValidPermissionInClaims()
    {
        // Arrange
        var userId = "auth0|789";
        var clubId = Guid.NewGuid();
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club);

        // 1. Create ClaimsPrincipal with 'sub' claim
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", userId)
        ], "TestAuth"));

        // 2. Setup HttpContext with Routing Feature
        var httpContext = new DefaultHttpContext();
        var routeValuesFeature = new RouteValuesFeature
        {
            RouteValues = new RouteValueDictionary { { "clubId", clubId.ToString() } }
        };
        httpContext.Features.Set<IRouteValuesFeature>(routeValuesFeature);

        _accessServiceMock
            .Setup(x => x.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Club, clubId, default))
            .ReturnsAsync(true);

        var authContext = new AuthorizationHandlerContext([requirement], user, httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotSucceed_WhenSubClaimIsMissing()
    {
        // Arrange
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club);

        // Empty principal without 'sub' or 'NameIdentifier'
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var httpContext = new DefaultHttpContext();

        var authContext = new AuthorizationHandlerContext([requirement], user, httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
        _accessServiceMock.Verify(x => x.HasAccessAsync(It.IsAny<string>(), It.IsAny<AppRole>(), It.IsAny<TargetScope>(), It.IsAny<Guid?>(), default), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotSucceed_WhenAccessServiceReturnsFalse()
    {
        // Arrange
        var userId = "auth0|denied-user";
        var requirement = new ScopePermissionRequirement(AppRole.FullControl, TargetScope.Club);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));
        var httpContext = new DefaultHttpContext();

        _accessServiceMock
            .Setup(x => x.HasAccessAsync(userId, It.IsAny<AppRole>(), It.IsAny<TargetScope>(), It.IsAny<Guid?>(), default))
            .ReturnsAsync(false);

        var authContext = new AuthorizationHandlerContext([requirement], user, httpContext);

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
        var requirement = new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Global);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)]));
        var httpContext = new DefaultHttpContext();

        _accessServiceMock
            .Setup(x => x.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Global, null, default))
            .ReturnsAsync(true);

        var authContext = new AuthorizationHandlerContext([requirement], user, httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
        _accessServiceMock.Verify(x => x.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Global, null, default), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldCorrectlyParseTeamIdFromRoute()
    {
        // Arrange
        var userId = "auth0|team-user";
        var teamId = Guid.NewGuid();
        var requirement = new ScopePermissionRequirement(AppRole.Editor, TargetScope.Team);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)]));
        var httpContext = new DefaultHttpContext();

        var routeValuesFeature = new RouteValuesFeature
        {
            RouteValues = new RouteValueDictionary { { "teamId", teamId.ToString() } }
        };
        httpContext.Features.Set<IRouteValuesFeature>(routeValuesFeature);

        _accessServiceMock
            .Setup(x => x.HasAccessAsync(userId, AppRole.Editor, TargetScope.Team, teamId, default))
            .ReturnsAsync(true);

        var authContext = new AuthorizationHandlerContext([requirement], user, httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ShouldFailImmediately_WhenResourceIdIsInvalidForScopedRequirement()
    {
        // Arrange
        var userId = "auth0|test-user";
        var requirement = new ScopePermissionRequirement(AppRole.Editor, TargetScope.Club);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)]));

        var httpContext = new DefaultHttpContext();
        // Providing an invalid GUID string to trigger the short-circuit
        var routeValuesFeature = new RouteValuesFeature
        {
            RouteValues = new RouteValueDictionary { { "clubId", "invalid-guid-format" } }
        };
        httpContext.Features.Set<IRouteValuesFeature>(routeValuesFeature);

        var authContext = new AuthorizationHandlerContext([requirement], user, httpContext);

        // Act
        await _handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
        // Verify that the database service was never called (short-circuit worked)
        _accessServiceMock.Verify(x => x.HasAccessAsync(
            It.IsAny<string>(),
            It.IsAny<AppRole>(),
            It.IsAny<TargetScope>(),
            It.IsAny<Guid?>(),
            default), Times.Never);
    }
}