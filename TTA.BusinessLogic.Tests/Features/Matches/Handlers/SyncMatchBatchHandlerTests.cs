using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;
using Match = TTA.DataAccess.Models.Match;


namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for the <see cref="SyncMatchBatchHandler"/> class.
/// </summary>
public class SyncMatchBatchHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<SyncMatchBatchHandler>> _loggerMock;
    private readonly SyncMatchBatchHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncMatchBatchHandlerTests"/> class.
    /// </summary>
    public SyncMatchBatchHandlerTests()
    {
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<SyncMatchBatchHandler>>();

        _handler = new SyncMatchBatchHandler(
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully processes batch synchronization when the match exists and data is valid.
    /// </summary>
    [Fact]
    public async Task Handle_Should_SyncBatch_When_MatchExists_And_DataIsValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };

        var eventReq = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false);

        var anchorReq = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: DateTime.UtcNow);

        var presenceReq = new CreatePlayerPresenceRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            TimeOut: null);

        var batchRequest = new MatchSyncBatchRequest(
            Events: [eventReq],
            Anchors: [anchorReq],
            Presences: [presenceReq]);

        var command = new SyncMatchBatchCommand(matchId, batchRequest);

        var projection = new MatchSyncBatchProjection(
            syncedEventIds: [eventReq.Id],
            syncedAnchorIds: [anchorReq.Id],
            syncedPresenceIds: [presenceReq.Id]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _matchRepositoryMock
            .Setup(r => r.SyncMatchBatchAsync(
                matchId,
                It.IsAny<IEnumerable<GameEvent>>(),
                It.IsAny<IEnumerable<TimeAnchor>>(),
                It.IsAny<IEnumerable<PlayerPresence>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MatchId.Should().Be(matchId);
        result.SyncedEventIds.Should().ContainSingle().Which.Should().Be(eventReq.Id);
        result.SyncedAnchorIds.Should().ContainSingle().Which.Should().Be(anchorReq.Id);
        result.SyncedPresenceIds.Should().ContainSingle().Which.Should().Be(presenceReq.Id);

        _matchRepositoryMock.Verify(r => r.SyncMatchBatchAsync(
            matchId,
            It.IsAny<IEnumerable<GameEvent>>(),
            It.IsAny<IEnumerable<TimeAnchor>>(),
            It.IsAny<IEnumerable<PlayerPresence>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the target match is missing.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_MatchNotFound()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var command = new SyncMatchBatchCommand(
            matchId,
            new MatchSyncBatchRequest([], [], []));

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {matchId} was not found.");

        _matchRepositoryMock.Verify(r => r.SyncMatchBatchAsync(
            It.IsAny<Guid>(),
            It.IsAny<IEnumerable<GameEvent>>(),
            It.IsAny<IEnumerable<TimeAnchor>>(),
            It.IsAny<IEnumerable<PlayerPresence>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a PostgreSQL foreign key violation occurs during execution.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_ForeignKey_Violation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var command = new SyncMatchBatchCommand(matchId, new MatchSyncBatchRequest([], [], []));
        var pgException = new PostgresException("FK Violation", "ERROR", "ERROR", "23503");

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _matchRepositoryMock
            .Setup(r => r.SyncMatchBatchAsync(
                matchId,
                It.IsAny<IEnumerable<GameEvent>>(),
                It.IsAny<IEnumerable<TimeAnchor>>(),
                It.IsAny<IEnumerable<PlayerPresence>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("One or more referenced records (Match Lineup, Event Definition, or Match) no longer exist.");
    }
}