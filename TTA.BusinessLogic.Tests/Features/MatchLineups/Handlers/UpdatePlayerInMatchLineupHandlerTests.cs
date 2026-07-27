using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.BusinessLogic.Features.MatchLineups.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Handlers;

/// <summary>
/// Unit tests for the <see cref="UpdatePlayerInMatchLineupHandler"/> ensuring correct record retrieval,
/// state updates, and proper handling of database exceptions.
/// </summary>
public class UpdatePlayerInMatchLineupHandlerTests
{
    private readonly Mock<IMatchLineupRepository> _matchLineupRepoMock;
    private readonly Mock<ILogger<UpdatePlayerInMatchLineupHandler>> _loggerMock;
    private readonly UpdatePlayerInMatchLineupHandler _handler;

    public UpdatePlayerInMatchLineupHandlerTests()
    {
        _matchLineupRepoMock = new Mock<IMatchLineupRepository>();
        _loggerMock = new Mock<ILogger<UpdatePlayerInMatchLineupHandler>>();

        _handler = new UpdatePlayerInMatchLineupHandler(
            _matchLineupRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a valid update request successfully updates the entity and returns its ID.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnId()
    {
        // Arrange
        var command = CreateCommand();
        var existingLineup = new MatchLineup { Id = command.Id, PlayerRosterId = Guid.NewGuid() };

        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLineup);

        _matchLineupRepoMock
            .Setup(x => x.UpsertLineupItemAsync(It.IsAny<MatchLineup>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLineup);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.Id);
        _matchLineupRepoMock.Verify(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a NotFoundException is thrown when the lineup entry does not exist in the database.
    /// </summary>
    [Fact]
    public async Task Handle_NonExistentEntry_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = CreateCommand();
        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchLineup?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match lineup entry with ID {command.Id} was not found.");
    }

    /// <summary>
    /// Verifies that a 23503 SQL state (Foreign Key Violation) throws a NotFoundException 
    /// indicating the specified position is invalid.
    /// </summary>
    [Fact]
    public async Task Handle_InvalidPositionId_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = CreateCommand();
        var existingLineup = new MatchLineup { Id = command.Id };
        var pgException = CreatePostgresException("23503", "Foreign key violation");

        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLineup);

        _matchLineupRepoMock
            .Setup(x => x.UpsertLineupItemAsync(It.IsAny<MatchLineup>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("The specified position does not exist.");
    }

    /// <summary>
    /// Verifies that a 23505 SQL state (Unique Violation) throws a ConflictException.
    /// This happens if the updated number or player is already present in the lineup.
    /// </summary>
    [Fact]
    public async Task Handle_DuplicateNumber_ShouldThrowConflictException()
    {
        // Arrange
        var command = CreateCommand();
        var existingLineup = new MatchLineup { Id = command.Id };
        var pgException = CreatePostgresException("23505", "Unique violation");

        _matchLineupRepoMock
            .Setup(x => x.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLineup);

        _matchLineupRepoMock
            .Setup(x => x.UpsertLineupItemAsync(It.IsAny<MatchLineup>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("This player is already registered in the lineup for this match.");
    }

    #region Helpers

    /// <summary>
    /// Creates a standard <see cref="UpdatePlayerInMatchLineupCommand"/> for testing purposes.
    /// </summary>
    private static UpdatePlayerInMatchLineupCommand CreateCommand() =>
        new(Guid.NewGuid(), 10, Guid.NewGuid());

    /// <summary>
    /// Helper method to create a <see cref="PostgresException"/> with a specific SQL state.
    /// </summary>
    private static PostgresException CreatePostgresException(string sqlState, string message)
    {
        return new PostgresException(
            messageText: message,
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
    }

    #endregion
}