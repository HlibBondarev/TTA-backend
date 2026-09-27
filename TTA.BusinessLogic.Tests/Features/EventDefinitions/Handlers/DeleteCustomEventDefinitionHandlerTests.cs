using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.BusinessLogic.Features.EventDefinitions.Handlers;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Handlers;

/// < summary >
/// Unit tests for < see cref="DeleteCustomEventDefinitionHandler"/ >.
/// < /summary >
public class DeleteCustomEventDefinitionHandlerTests
{
    private readonly Mock<IEventDefinitionRepository> _repositoryMock;
    private readonly Mock<ILogger<DeleteCustomEventDefinitionHandler>> _loggerMock;
    private readonly DeleteCustomEventDefinitionHandler _handler;

    public DeleteCustomEventDefinitionHandlerTests()
    {
        _repositoryMock = new Mock<IEventDefinitionRepository>();
        _loggerMock = new Mock<ILogger<DeleteCustomEventDefinitionHandler>>();
        _handler = new DeleteCustomEventDefinitionHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// < summary >
    /// Verifies that < see cref="DeleteCustomEventDefinitionHandler.Handle"/ > returns true 
    /// when the target custom event definition is successfully soft-deleted by its owner.
    /// < /summary >
    [Fact]
    public async Task Handle_ShouldReturnTrue_WhenSoftDeleteSucceeds()
    {
        // Arrange
        var command = new DeleteCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            UserId: "auth0|user123");

        _repositoryMock
            .Setup(r => r.SoftDeleteAsync(command.Id, command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        _repositoryMock.Verify(r => r.SoftDeleteAsync(command.Id, command.UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// < summary >
    /// Verifies that < see cref="DeleteCustomEventDefinitionHandler.Handle"/ > returns false 
    /// when the custom event definition is not found or not owned by the specified user.
    /// < /summary >
    [Fact]
    public async Task Handle_ShouldReturnFalse_WhenSoftDeleteFails()
    {
        // Arrange
        var command = new DeleteCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            UserId: "auth0|otherUser");

        _repositoryMock
            .Setup(r => r.SoftDeleteAsync(command.Id, command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();

        _repositoryMock.Verify(r => r.SoftDeleteAsync(command.Id, command.UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// < summary >
    /// Verifies that < see cref="DeleteCustomEventDefinitionHandler.Handle"/ > passes 
    /// the provided cancellation token directly to the repository layer call.
    /// < /summary >
    [Fact]
    public async Task Handle_ShouldPassCancellationTokenToRepository()
    {
        // Arrange
        var command = new DeleteCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            UserId: "auth0|user123");

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _repositoryMock
            .Setup(r => r.SoftDeleteAsync(command.Id, command.UserId, cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(command, cancellationToken);

        // Assert
        _repositoryMock.Verify(r => r.SoftDeleteAsync(command.Id, command.UserId, cancellationToken), Times.Once);
    }
}