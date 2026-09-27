using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.BusinessLogic.Features.MatchLineups.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Handlers;

/// <summary>
/// Unit tests for the <see cref="DeletePlayerFromMatchLineupHandler"/> ensuring correct 
/// existence validation, data integrity checks, and proper repository interaction.
/// </summary>
public class DeletePlayerFromMatchLineupHandlerTests
{
    private readonly Mock<IMatchLineupRepository> _matchLineupRepoMock;
    private readonly Mock<ILogger<DeletePlayerFromMatchLineupHandler>> _loggerMock;
    private readonly DeletePlayerFromMatchLineupHandler _handler;

    public DeletePlayerFromMatchLineupHandlerTests()
    {
        _matchLineupRepoMock = new Mock<IMatchLineupRepository>();
        _loggerMock = new Mock<ILogger<DeletePlayerFromMatchLineupHandler>>();

        _handler = new DeletePlayerFromMatchLineupHandler(
            _matchLineupRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully deletes an existing record 
    /// when no linked game events are found.
    /// </summary>
    [Fact]
    public async Task Handle_ExistingEntryAndNoEvents_ShouldReturnTrue()
    {
        // Arrange
        var command = new DeletePlayerFromMatchLineupCommand(Guid.NewGuid());
        var existingEntry = new MatchLineup { Id = command.Id };

        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        // Explicitly setup that NO linked events exist to satisfy the business rule
        _matchLineupRepoMock
            .Setup(x => x.HasLinkedEventsAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _matchLineupRepoMock
            .Setup(x => x.DeleteLineupItemAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _matchLineupRepoMock.Verify(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
        _matchLineupRepoMock.Verify(x => x.HasLinkedEventsAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
        _matchLineupRepoMock.Verify(x => x.DeleteLineupItemAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="ConflictException"/> 
    /// if the lineup entry has associated game events, blocking the deletion.
    /// </summary>
    [Fact]
    public async Task Handle_WithLinkedEvents_ShouldThrowConflictException()
    {
        // Arrange
        var command = new DeletePlayerFromMatchLineupCommand(Guid.NewGuid());
        var existingEntry = new MatchLineup { Id = command.Id };

        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        // Mock that linked events exist
        _matchLineupRepoMock
            .Setup(x => x.HasLinkedEventsAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("This player cannot be removed from the lineup because there are game events (e.g., goals or cards) linked to them.");

        // Verification: Delete must NEVER be called if business rule validation fails
        _matchLineupRepoMock.Verify(x => x.DeleteLineupItemAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> 
    /// if the record to be deleted does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_NonExistentEntry_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new DeletePlayerFromMatchLineupCommand(Guid.NewGuid());

        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchLineup?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match lineup entry with ID {command.Id} was not found.");

        // Verification: No further checks should occur if item is not found
        _matchLineupRepoMock.Verify(x => x.HasLinkedEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _matchLineupRepoMock.Verify(x => x.DeleteLineupItemAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler returns false if the repository fails to delete 
    /// an existing record after passing all validations.
    /// </summary>
    [Fact]
    public async Task Handle_RepositoryReturnsFalse_ShouldReturnFalse()
    {
        // Arrange
        var command = new DeletePlayerFromMatchLineupCommand(Guid.NewGuid());
        var existingEntry = new MatchLineup { Id = command.Id };

        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _matchLineupRepoMock
            .Setup(x => x.HasLinkedEventsAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _matchLineupRepoMock
            .Setup(x => x.DeleteLineupItemAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _matchLineupRepoMock.Verify(x => x.DeleteLineupItemAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}