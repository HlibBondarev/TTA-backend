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
/// Unit tests for the <see cref="CreateGameEventHandler"/> class.
/// Ensures validation logic, repository interaction, and exception mapping are correct.
/// </summary>
public class CreateGameEventHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<IMatchLineupRepository> _matchLineupRepositoryMock;
    private readonly Mock<ILogger<CreateGameEventHandler>> _loggerMock;
    private readonly CreateGameEventHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateGameEventHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public CreateGameEventHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _matchLineupRepositoryMock = new Mock<IMatchLineupRepository>();
        _loggerMock = new Mock<ILogger<CreateGameEventHandler>>();

        _handler = new CreateGameEventHandler(
            _gameEventRepositoryMock.Object,
            _matchLineupRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully creates a game event when the player belongs to the match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CreateEvent_When_DataIsValid_And_PlayerInMatch()
    {
        // Arrange
        var command = CreateCommand();
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = command.MatchId };
        var createdEvent = new GameEvent { Id = Guid.NewGuid() };

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<GameEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdEvent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(createdEvent.Id);
        _gameEventRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<GameEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="ConflictException"/> when the player (MatchLineup) 
    /// does not belong to the specified match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_PlayerNotInMatch()
    {
        // Arrange
        var command = CreateCommand();
        var differentMatchId = Guid.NewGuid();
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = differentMatchId };

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The specified player is not registered in this match's protocol.");
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the MatchLineup entry 
    /// cannot be found in the database.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_LineupNotFound()
    {
        // Arrange
        var command = CreateCommand();

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchLineup?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("The specified player protocol entry was not found.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a PostgreSQL foreign key violation occurs.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_ForeignKey_Violation()
    {
        // Arrange
        var command = CreateCommand();
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = command.MatchId };
        var pgException = new PostgresException("FK Violation", "ERROR", "ERROR", "23503");

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<GameEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Related record (Event Definition or Match Lineup) no longer exists.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a PostgreSQL custom business rule (P0001) is triggered.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_BusinessRule_Violation()
    {
        // Arrange
        var command = CreateCommand();
        var matchLineup = new MatchLineup { Id = command.MatchLineupId, MatchId = command.MatchId };
        const string dbErrorMessage = "Event timestamp cannot be in the future";

        // Creating PostgresException with state P0001
        var pgException = new PostgresException(dbErrorMessage, "ERROR", "ERROR", "P0001")
        {
            // MessageText is internal or read-only in some versions/mocks, but Dapper/Npgsql handles this via ctor or properties
        };

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<GameEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>();
    }

    /// <summary>
    /// Helper method to create a valid <see cref="CreateGameEventCommand"/>.
    /// </summary>
    /// <returns>A populated command instance.</returns>
    private static CreateGameEventCommand CreateCommand()
    {
        return new CreateGameEventCommand(
            MatchId: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false);
    }
}