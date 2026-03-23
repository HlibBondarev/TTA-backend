using Moq;
using TTA.BusinessLogic.Services;
using TTA.Common.Enums;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Tests.Services.Auth;

public class AccessServiceTests
{
    private readonly Mock<IAccessRepository> _accessRepositoryMock;
    private readonly AccessService _service;

    public AccessServiceTests()
    {
        _accessRepositoryMock = new Mock<IAccessRepository>();
        _service = new AccessService(_accessRepositoryMock.Object);
    }

    [Fact]
    public async Task HasAccessAsync_ShouldReturnTrue_WhenUserHasExactRole()
    {
        // Arrange
        var userId = "auth0|test-user";
        var targetId = Guid.NewGuid();
        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(userId, TargetScope.Club, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppRole.Editor);

        // Act
        var result = await _service.HasAccessAsync(userId, AppRole.Editor, TargetScope.Club, targetId);

        // Assert
        Assert.True(result);
        _accessRepositoryMock.Verify(x =>
            x.GetUserRoleForScope(userId, TargetScope.Club, targetId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HasAccessAsync_ShouldReturnTrue_WhenUserHasHigherRoleInHierarchy()
    {
        // Arrange
        var userId = "auth0|admin-user";
        var targetId = Guid.NewGuid();

        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(userId, TargetScope.Club, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppRole.FullControl);

        // Act
        var result = await _service.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Club, targetId);

        // Assert
        Assert.True(result);
        _accessRepositoryMock.Verify(x =>
            x.GetUserRoleForScope(userId, TargetScope.Club, targetId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HasAccessAsync_ShouldReturnFalse_WhenUserHasLowerRole()
    {
        // Arrange
        var userId = "auth0|viewer-user";
        var targetId = Guid.NewGuid();

        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(userId, TargetScope.Club, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppRole.Viewer);

        // Act
        var result = await _service.HasAccessAsync(userId, AppRole.Editor, TargetScope.Club, targetId);

        // Assert
        Assert.False(result);
        _accessRepositoryMock.Verify(x =>
            x.GetUserRoleForScope(userId, TargetScope.Club, targetId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HasAccessAsync_ShouldReturnFalse_WhenNoRoleIsAssigned()
    {
        // Arrange
        var userId = "unknown-user";
        var scope = TargetScope.Global;
        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(userId, scope, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppRole?)null);

        // Act
        var result = await _service.HasAccessAsync(userId, AppRole.Viewer, scope);

        // Assert
        Assert.False(result);
        _accessRepositoryMock.Verify(x =>
            x.GetUserRoleForScope(userId, scope, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}