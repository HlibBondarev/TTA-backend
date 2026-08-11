using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Handlers;

/// <summary>
/// Unit tests for the <see cref="UpdateGameEventHandler"/> class.
/// Ensures validation logic, repository interaction, and exception mapping match the handler implementation.
/// </summary>
public class UpdateGameEventHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<IMatchLineupRepository> _matchLineupRepositoryMock;
    private readonly Mock<ILogger<UpdateGameEventHandler>> _loggerMock;
    private readonly UpdateGameEventHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateGameEventHandlerTests"/> class.
    /// </summary>
    public UpdateGameEventHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _matchLineupRepositoryMock = new Mock<IMatchLineupRepository>();
        _loggerMock = new Mock<ILogger<UpdateGameEventHandler>>();

        _handler = new UpdateGameEventHandler(
            _gameEventRepositoryMock.Object,
            _matchLineupRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully updates a game event when the data is valid
    /// and the player belongs to the correct match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_UpdateEvent_When_DataIsValid()
    {
        // Arrange
        var command = CreateCommand();
        var existingEvent = new GameEvent { Id = command.Id };
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = command.MatchId };

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<GameEvent>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingEvent]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.Id);
        _gameEventRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<IEnumerable<GameEvent>>(events =>
                events.Count() == 1 &&
                events.Single().Id == command.Id &&
                events.Single().MatchLineupId == command.MatchLineupId &&
                events.Single().EventDefinitionId == command.EventDefinitionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown with the correct message
    /// when the event ID does not exist in the database.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_EventDoesNotExist()
    {
        // Arrange
        var command = CreateCommand();

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameEvent?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Game event with ID {command.Id} was not found.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when the lineup entry 
    /// is not part of the specified match protocol.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_PlayerNotInMatch()
    {
        // Arrange
        var command = CreateCommand();
        var existingEvent = new GameEvent { Id = command.Id };
        var differentMatchId = Guid.NewGuid();
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = differentMatchId };

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The selected player is not part of this match's protocol.");
    }

    /// <summary>
    /// Verifies that a <see cref="PostgresException"/> is caught and rethrown as <see cref="ConflictException"/>
    /// for foreign key violations (SQL State 23503).
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_ForeignKey_Violation()
    {
        // Arrange
        var command = CreateCommand();
        var existingEvent = new GameEvent { Id = command.Id };
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = command.MatchId };
        var pgException = new PostgresException("FK Violation", "ERROR", "ERROR", "23503");

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<GameEvent>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Related record (Event Definition or Match Lineup) no longer exists.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a database business rule (P0001)
    /// is violated during the update.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_BusinessRule_Violation()
    {
        // Arrange
        var command = CreateCommand();
        var existingEvent = new GameEvent { Id = command.Id };
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = command.MatchId };
        var pgException = new PostgresException("Business rule violation", "ERROR", "ERROR", "P0001");

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<GameEvent>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>();
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when UpsertAsync returns an empty result set.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_UpsertAsyncReturnsEmpty()
    {
        // Arrange
        var command = CreateCommand();
        var existingEvent = new GameEvent { Id = command.Id };
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = command.MatchId };

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<GameEvent>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Game event with ID {command.Id} was not found.");
    }

    /// <summary>
    /// Helper method to create a valid <see cref="UpdateGameEventCommand"/> for testing.
    /// </summary>
    /// <returns>A populated command instance.</returns>
    private static UpdateGameEventCommand CreateCommand()
    {
        return new UpdateGameEventCommand(
            Id: Guid.NewGuid(),
            MatchId: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            IsLeadToGoal: false);
    }
}