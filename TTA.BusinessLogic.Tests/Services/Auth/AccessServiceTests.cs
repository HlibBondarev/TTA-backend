using Moq;
using TTA.BusinessLogic.Services;
using TTA.Common.Enums;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Tests.Services.Auth;

/// <summary>
/// Contains unit tests for the AccessService to verify role-based access logic and hierarchy.
/// </summary>
public class AccessServiceTests
{
    private readonly Mock<IAccessRepository> _accessRepositoryMock;
    private readonly AccessService _service;

    public AccessServiceTests()
    {
        _accessRepositoryMock = new Mock<IAccessRepository>();
        _service = new AccessService(_accessRepositoryMock.Object);
    }

    /// <summary>
    /// Verifies that access is granted when the user has the exact required role.
    /// </summary>
    [Fact]
    public async Task HasAccessAsync_ShouldReturnTrue_WhenUserHasExactRole()
    {
        // Arrange
        var userId = "auth0|test-user";
        var targetId = Guid.NewGuid();
        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(userId, TargetScope.Club.ToString(), targetId))
            .ReturnsAsync(AppRole.Editor.ToString());

        // Act
        var result = await _service.HasAccessAsync(userId, AppRole.Editor, TargetScope.Club, targetId);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies the hierarchy: a user with FullControl should have access to Viewer-level resources.
    /// </summary>
    [Fact]
    public async Task HasAccessAsync_ShouldReturnTrue_WhenUserHasHigherRoleInHierarchy()
    {
        // Arrange
        var userId = "auth0|admin-user";
        var targetId = Guid.NewGuid();

        // User is FullControl (0)
        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(userId, TargetScope.Club.ToString(), targetId))
            .ReturnsAsync(AppRole.FullControl.ToString());

        // Act
        // Requirement is only Viewer (2)
        var result = await _service.HasAccessAsync(userId, AppRole.Viewer, TargetScope.Club, targetId);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that access is denied when the user's role is lower than the required role.
    /// </summary>
    [Fact]
    public async Task HasAccessAsync_ShouldReturnFalse_WhenUserHasLowerRole()
    {
        // Arrange
        var userId = "auth0|viewer-user";
        var targetId = Guid.NewGuid();

        // User is Viewer (2)
        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(userId, TargetScope.Club.ToString(), targetId))
            .ReturnsAsync(AppRole.Viewer.ToString());

        // Act
        // Requirement is Editor (1)
        var result = await _service.HasAccessAsync(userId, AppRole.Editor, TargetScope.Club, targetId);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that access is denied if the repository returns no role for the user.
    /// </summary>
    [Fact]
    public async Task HasAccessAsync_ShouldReturnFalse_WhenNoRoleIsAssigned()
    {
        // Arrange
        _accessRepositoryMock
            .Setup(x => x.GetUserRoleForScope(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.HasAccessAsync("unknown-user", AppRole.Viewer, TargetScope.Global);

        // Assert
        Assert.False(result);
    }
}