using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.TimeAnchors.Commands;
using TTA.BusinessLogic.Features.TimeAnchors.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.TimeAnchors.Handlers;

/// <summary>
/// Unit tests for the <see cref="DeleteTimeAnchorHandler"/> class.
/// Validates existence checks and the repository call flow during time anchor deletion.
/// </summary>
public class DeleteTimeAnchorHandlerTests
{
    private readonly Mock<ITimeAnchorRepository> _timeAnchorRepositoryMock;
    private readonly Mock<ILogger<DeleteTimeAnchorHandler>> _loggerMock;
    private readonly DeleteTimeAnchorHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteTimeAnchorHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public DeleteTimeAnchorHandlerTests()
    {
        _timeAnchorRepositoryMock = new Mock<ITimeAnchorRepository>();
        _loggerMock = new Mock<ILogger<DeleteTimeAnchorHandler>>();

        _handler = new DeleteTimeAnchorHandler(
            _timeAnchorRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully deletes a time anchor when it exists.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnTrue_When_AnchorIsDeletedSuccessfully()
    {
        // Arrange
        var command = new DeleteTimeAnchorCommand(Guid.NewGuid());
        var existingAnchor = new TimeAnchor { Id = command.Id };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchor);

        _timeAnchorRepositoryMock
            .Setup(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _timeAnchorRepositoryMock.Verify(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> 
    /// when attempting to delete a non-existent time anchor.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_AnchorDoesNotExist()
    {
        // Arrange
        var command = new DeleteTimeAnchorCommand(Guid.NewGuid());

        _timeAnchorRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeAnchor?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Time anchor with ID {command.Id} was not found.");

        _timeAnchorRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler returns false if the repository fails to delete 
    /// an existing anchor (e.g., database constraint or zero rows affected).
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnFalse_When_RepositoryDeletionFails()
    {
        // Arrange
        var command = new DeleteTimeAnchorCommand(Guid.NewGuid());
        var existingAnchor = new TimeAnchor { Id = command.Id };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchor);

        _timeAnchorRepositoryMock
            .Setup(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _timeAnchorRepositoryMock.Verify(r => r.DeleteAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}