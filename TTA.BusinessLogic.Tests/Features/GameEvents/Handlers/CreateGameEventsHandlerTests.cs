using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Handlers;

/// <summary>
/// Unit tests for the <see cref="CreateGameEventsHandler"/> class.
/// Ensures batch validation logic, repository interaction, and exception mapping are correct.
/// </summary>
public class CreateGameEventsHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<IMatchLineupRepository> _matchLineupRepositoryMock;
    private readonly Mock<ILogger<CreateGameEventsHandler>> _loggerMock;
    private readonly CreateGameEventsHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateGameEventsHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public CreateGameEventsHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _matchLineupRepositoryMock = new Mock<IMatchLineupRepository>();
        _loggerMock = new Mock<ILogger<CreateGameEventsHandler>>();

        _handler = new CreateGameEventsHandler(
            _gameEventRepositoryMock.Object,
            _matchLineupRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully creates a batch of game events when all players belong to the match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CreateEvent_When_DataIsValid_And_PlayerInMatch()
    {
        // Arrange
        var (command, request) = CreateCommand();
        var matchLineup = new MatchLineup { Id = request.MatchLineupId, MatchId = command.MatchId };
        var createdEvents = new List<GameEvent> { request.ToModel() };

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(request.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<GameEvent>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdEvents);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(request.Id);
        _gameEventRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<IEnumerable<GameEvent>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="ConflictException"/> when a player (MatchLineup) 
    /// does not belong to the specified match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_PlayerNotInMatch()
    {
        // Arrange
        var (command, request) = CreateCommand();
        var differentMatchId = Guid.NewGuid();
        var matchLineup = new MatchLineup { Id = request.MatchLineupId, MatchId = differentMatchId };

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(request.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The specified player is not registered in this match's protocol.");
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when a MatchLineup entry 
    /// cannot be found in the database.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_LineupNotFound()
    {
        // Arrange
        var (command, request) = CreateCommand();

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(request.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchLineup?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"The specified player protocol entry {request.MatchLineupId} was not found.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a PostgreSQL foreign key violation occurs during batch execution.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_ForeignKey_Violation()
    {
        // Arrange
        var (command, request) = CreateCommand();
        var matchLineup = new MatchLineup { Id = request.MatchLineupId, MatchId = command.MatchId };
        var pgException = new PostgresException("FK Violation", "ERROR", "ERROR", "23503");

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(request.MatchLineupId, It.IsAny<CancellationToken>()))
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
    /// Verifies that a <see cref="ConflictException"/> is thrown when a PostgreSQL custom business rule (P0001) is triggered during batch execution.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_BusinessRule_Violation()
    {
        // Arrange
        var (command, request) = CreateCommand();
        var matchLineup = new MatchLineup { Id = request.MatchLineupId, MatchId = command.MatchId };
        const string dbErrorMessage = "Event timestamp cannot be in the future";

        var pgException = new PostgresException(dbErrorMessage, "ERROR", "ERROR", "P0001");

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(request.MatchLineupId, It.IsAny<CancellationToken>()))
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
    /// Verifies that the handler successfully creates a multi-item batch of game events in a single repository invocation.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CreateEvents_When_BatchIsValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var request1 = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false);

        var request2 = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow.AddSeconds(10),
            IsLeadToGoal: true);

        var command = new CreateGameEventsCommand(matchId, [request1, request2]);

        var matchLineup1 = new MatchLineup { Id = request1.MatchLineupId, MatchId = matchId };
        var matchLineup2 = new MatchLineup { Id = request2.MatchLineupId, MatchId = matchId };

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(request1.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup1);

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(request2.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchLineup2);

        var createdEvents = new List<GameEvent> { request1.ToModel(), request2.ToModel() };

        _gameEventRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<GameEvent>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdEvents);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(2);
        resultList.Should().Contain([request1.Id, request2.Id]);

        _gameEventRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<IEnumerable<GameEvent>>(events => events.Count() == 2),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that if any item in a batch refers to an invalid lineup, validation fails and UpsertAsync is never called.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Not_Call_UpsertAsync_When_BatchContainsInvalidLineup()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var validRequest = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false);

        var invalidRequest = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow.AddSeconds(10),
            IsLeadToGoal: false);

        var command = new CreateGameEventsCommand(matchId, [validRequest, invalidRequest]);

        var validLineup = new MatchLineup { Id = validRequest.MatchLineupId, MatchId = matchId };

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(validRequest.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validLineup);

        _matchLineupRepositoryMock
            .Setup(r => r.GetByIdAsync(invalidRequest.MatchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchLineup?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();

        _gameEventRepositoryMock.Verify(r => r.UpsertAsync(
            It.IsAny<IEnumerable<GameEvent>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Helper method to create a valid <see cref="CreateGameEventsCommand"/> and its underlying request DTO.
    /// </summary>
    /// <returns>A tuple containing the command and the single request item.</returns>
    private static (CreateGameEventsCommand Command, CreateGameEventRequest Request) CreateCommand()
    {
        var matchId = Guid.NewGuid();
        var request = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false);

        var command = new CreateGameEventsCommand(matchId, [request]);
        return (command, request);
    }
}