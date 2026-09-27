using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Clubs.Commands;
using TTA.BusinessLogic.Features.Clubs.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Clubs.Handlers;

/// <summary>
/// Unit tests for the CreateClubHandler to ensure correct business logic execution.
/// </summary>
public class CreateClubHandlerTests
{
    private readonly Mock<IClubRepository> _repositoryMock;
    private readonly Mock<ILogger<CreateClubHandler>> _loggerMock;
    private readonly CreateClubHandler _handler;

    public CreateClubHandlerTests()
    {
        _repositoryMock = new Mock<IClubRepository>();
        _loggerMock = new Mock<ILogger<CreateClubHandler>>();
        _handler = new CreateClubHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_UserAlreadyHasClub_ThrowsConflictException()
    {
        // Arrange
        // Updated command with all required identity parameters
        var command = new CreateClubCommand(
            "Test Club",
            Guid.NewGuid(),
            "auth0|123",
            "test@example.com",
            "Test User");

        _repositoryMock.Setup(r => r.HasExistingClubOwnershipAsync(
                command.CreatorUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsNewGuid()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        // Updated command construction according to the new signature
        var command = new CreateClubCommand(
            "New Club",
            Guid.NewGuid(),
            "auth0|456",
            "new_user@example.com",
            "New Club Owner");

        _repositoryMock.Setup(r => r.HasExistingClubOwnershipAsync(
                command.CreatorUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Setup the mock to match the new repository method signature
        _repositoryMock.Setup(r => r.CreateWithOwnershipAsync(
                It.IsAny<Club>(),
                command.CreatorUserId,
                command.CreatorEmail,
                command.CreatorDisplayName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(expectedId, result);

        // Verify that the repository was called exactly once with correct parameters
        _repositoryMock.Verify(r => r.CreateWithOwnershipAsync(
            It.Is<Club>(c => c.Name == command.Name && c.CityId == command.CityId),
            command.CreatorUserId,
            command.CreatorEmail,
            command.CreatorDisplayName,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}