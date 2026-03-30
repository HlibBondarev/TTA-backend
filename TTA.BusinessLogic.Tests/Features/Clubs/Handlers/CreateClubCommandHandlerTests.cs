using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Clubs.Commands;
using TTA.BusinessLogic.Features.Clubs.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Clubs.Handlers;

public class CreateClubCommandHandlerTests
{
    private readonly Mock<IClubRepository> _repositoryMock;
    private readonly Mock<ILogger<CreateClubCommandHandler>> _loggerMock;
    private readonly CreateClubCommandHandler _handler;

    public CreateClubCommandHandlerTests()
    {
        _repositoryMock = new Mock<IClubRepository>();
        _loggerMock = new Mock<ILogger<CreateClubCommandHandler>>();
        _handler = new CreateClubCommandHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_UserAlreadyHasClub_ThrowsConflictException()
    {
        // Arrange
        var command = new CreateClubCommand("Test Club", Guid.NewGuid(), "auth0|123");
        _repositoryMock.Setup(r => r.HasExistingClubOwnershipAsync(command.CreatorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsNewGuid()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        var command = new CreateClubCommand("New Club", Guid.NewGuid(), "auth0|456");

        _repositoryMock.Setup(r => r.HasExistingClubOwnershipAsync(command.CreatorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repositoryMock.Setup(r => r.CreateWithOwnershipAsync(It.IsAny<Club>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(expectedId, result);
        _repositoryMock.Verify(r => r.CreateWithOwnershipAsync(It.IsAny<Club>(), command.CreatorUserId, It.IsAny<CancellationToken>()), Times.Once);
    }
}