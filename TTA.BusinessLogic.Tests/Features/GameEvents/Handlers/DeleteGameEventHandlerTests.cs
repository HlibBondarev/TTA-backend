using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Handlers;

/// <summary>
/// Unit tests for the <see cref="DeleteGameEventHandler"/> class.
/// Validates existence checks and the repository call flow during event deletion.
/// </summary>
public class DeleteGameEventHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<ILogger<DeleteGameEventHandler>> _loggerMock;
    private readonly DeleteGameEventHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteGameEventHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public DeleteGameEventHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _loggerMock = new Mock<ILogger<DeleteGameEventHandler>>();

        _handler = new DeleteGameEventHandler(
            _gameEventRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully deletes a game event when it exists.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnTrue_When_EventIsDeletedSuccessfully()
    {
        // Arrange
        var command = new DeleteGameEventCommand(Guid.NewGuid());
        var existingEvent = new GameEvent { Id = command.Id };

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        _gameEventRepositoryMock
            .Setup(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _gameEventRepositoryMock.Verify(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> 
    /// when attempting to delete a non-existent game event.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_EventDoesNotExist()
    {
        // Arrange
        var command = new DeleteGameEventCommand(Guid.NewGuid());

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameEvent?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Game event with ID {command.Id} was not found.");

        _gameEventRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler returns false if the repository fails to delete 
    /// an existing event (e.g., database constraint or zero rows affected).
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnFalse_When_RepositoryDeletionFails()
    {
        // Arrange
        var command = new DeleteGameEventCommand(Guid.NewGuid());
        var existingEvent = new GameEvent { Id = command.Id };

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        _gameEventRepositoryMock
            .Setup(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _gameEventRepositoryMock.Verify(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}