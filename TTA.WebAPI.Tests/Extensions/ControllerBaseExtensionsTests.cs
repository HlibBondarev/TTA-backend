using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Authentication;
using System.Security.Claims;
using TTA.BusinessLogic.Services;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Extensions;

namespace TTA.WebAPI.Tests.Extensions;

public class ControllerBaseExtensionsTests
{
    private readonly TestController _controller;
    private readonly Auth0Settings _auth0Settings;
    private const string TestNamespace = "https://tta-api.com/";

    public ControllerBaseExtensionsTests()
    {
        _auth0Settings = new Auth0Settings(
        Authority: "https://test.auth0.com/",
        ClientId: "test-client",
        Audience: "test-api",
        Namespace: TestNamespace);

        _controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    // Dummy controller for testing extensions
    private class TestController : ControllerBase { }

    [Fact]
    public void GetUserClaims_ShouldReturnClaims_WhenAllClaimsArePresent()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "auth0|123"),
            new($"{TestNamespace}{IdentityResourceClaimsTypes.Email}", "test@example.com"),
            new($"{TestNamespace}display_name", "Test User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.HttpContext.User = new ClaimsPrincipal(identity);

        // Act
        var result = _controller.GetUserClaims(_auth0Settings);

        // Assert
        Assert.Equal("auth0|123", result.Id);
        Assert.Equal("test@example.com", result.Email);
        Assert.Equal("Test User", result.Name);
    }

    [Fact]
    public void GetUserClaims_ShouldFallbackToStandardClaims_WhenNamespaceClaimsAreMissing()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(IdentityResourceClaimsTypes.Sub, "sub-456"),
            new(ClaimTypes.Email, "standard@example.com"),
            new(ClaimTypes.Name, "Standard Name")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.HttpContext.User = new ClaimsPrincipal(identity);

        // Act
        var result = _controller.GetUserClaims(_auth0Settings);

        // Assert
        Assert.Equal("sub-456", result.Id);
        Assert.Equal("standard@example.com", result.Email);
        Assert.Equal("Standard Name", result.Name);
    }

    [Fact]
    public void GetUserId_ShouldReturnId_WhenSubExists()
    {
        // Arrange
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-789") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.HttpContext.User = new ClaimsPrincipal(identity);

        // Act
        var result = _controller.GetUserId(_auth0Settings);

        // Assert
        Assert.Equal("user-789", result);
    }

    [Fact]
    public void GetUserId_ShouldThrowAuthenticationException_WhenIdIsMissing()
    {
        // Arrange
        _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        // Act & Assert
        var exception = Assert.Throws<AuthenticationException>(() =>
            _controller.GetUserId(_auth0Settings));

        Assert.Contains(IdentityResourceClaimsTypes.Sub, exception.Message);
    }

    [Fact]
    public void GetUserClaims_ShouldReturnEmptyStrings_WhenNoClaimsPresent()
    {
        // Arrange
        _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = _controller.GetUserClaims(_auth0Settings);

        // Assert
        Assert.Equal(string.Empty, result.Id);
        Assert.Equal(string.Empty, result.Email);
        Assert.Equal(string.Empty, result.Name);
    }
}