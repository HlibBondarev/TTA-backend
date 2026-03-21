using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TTA.Common.Extensions;
using TTA.Common.Services.Api;
using TTA.Common.Services.DTOs;

namespace TTA.Common.Tests.Extensions;

public class ControllerBaseExtensionsTests
{
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly TestController _controller;

    public ControllerBaseExtensionsTests()
    {
        _userServiceMock = new Mock<ICurrentUserService>();
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
    public async Task GetUserClaims_ShouldReturnClaims_WhenHeaderExists()
    {
        // Arrange
        var token = "Bearer some-token";
        var expectedDto = new UserFromClaimsDto("id-123", "test@test.com", "Name");
        _controller.Request.Headers["Authorization"] = token;

        _userServiceMock.Setup(s => s.GetUserPropertiesFromClaims(token))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.GetUserClaims(_userServiceMock.Object);

        // Assert
        Assert.Equal(expectedDto, result);
    }

    [Fact]
    public async Task GetUserClaims_ShouldThrowInvalidOperationException_WhenHeaderIsMissing()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _controller.GetUserClaims(_userServiceMock.Object));
    }

    [Fact]
    public async Task GetUserId_ShouldReturnId_WhenClaimsAreValid()
    {
        // Arrange
        var token = "Bearer some-token";
        var expectedDto = new UserFromClaimsDto("user-unique-id", "test@test.com", "Name");
        _controller.Request.Headers["Authorization"] = token;

        _userServiceMock.Setup(s => s.GetUserPropertiesFromClaims(token))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.GetUserId(_userServiceMock.Object);

        // Assert
        Assert.Equal("user-unique-id", result);
    }
}