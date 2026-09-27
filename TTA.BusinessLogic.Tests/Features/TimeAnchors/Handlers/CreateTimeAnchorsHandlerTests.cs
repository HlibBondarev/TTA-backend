using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.TimeAnchors.Commands;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.TimeAnchors.Handlers;

/// <summary>
/// Unit tests for the <see cref="CreateTimeAnchorsHandler"/> class.
/// Ensures batch validation logic, state machine sequence rules, repository interaction, and exception mapping are correct.
/// </summary>
public class CreateTimeAnchorsHandlerTests
{
    private readonly Mock<ITimeAnchorRepository> _timeAnchorRepositoryMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<CreateTimeAnchorsHandler>> _loggerMock;
    private readonly CreateTimeAnchorsHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTimeAnchorsHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public CreateTimeAnchorsHandlerTests()
    {
        _timeAnchorRepositoryMock = new Mock<ITimeAnchorRepository>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<CreateTimeAnchorsHandler>>();

        _handler = new CreateTimeAnchorsHandler(
            _timeAnchorRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully creates time anchors using client-supplied IDs and Timestamps when the sequence is valid.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CreateAnchor_When_SequenceIsValid()
    {
        // Arrange
        var (command, request) = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };
        var createdAnchor = request.ToModel(command.MatchId);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([createdAnchor]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(request.Id);

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<IEnumerable<TimeAnchor>>(anchors =>
                anchors.Single().Id == request.Id &&
                anchors.Single().MatchId == command.MatchId &&
                anchors.Single().PeriodNumber == request.PeriodNumber &&
                anchors.Single().Type == request.Type &&
                anchors.Single().Timestamp == request.Timestamp.ToUniversalTime()),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that timestamps with different <see cref="DateTimeKind"/> values (Unspecified, Local, Utc) 
    /// are correctly normalized to UTC when mapped to the domain model during anchor creation.
    /// </summary>
    /// <param name="kind">The DateTimeKind value to test.</param>
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task Handle_Should_NormalizeTimestampToUtc_ForAnyDateTimeKind(DateTimeKind kind)
    {
        // Arrange
        var rawTimestamp = new DateTime(2026, 8, 8, 12, 0, 0, kind);
        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: rawTimestamp);

        var command = new CreateTimeAnchorsCommand(Guid.NewGuid(), [request]);
        var match = new Match { Id = command.MatchId };

        var expectedUtcTimestamp = kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(rawTimestamp, DateTimeKind.Utc),
            _ => rawTimestamp.ToUniversalTime()
        };

        var createdAnchor = new TimeAnchor
        {
            Id = request.Id,
            MatchId = command.MatchId,
            PeriodNumber = request.PeriodNumber,
            Type = request.Type,
            Timestamp = expectedUtcTimestamp
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([createdAnchor]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(request.Id);

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<IEnumerable<TimeAnchor>>(anchors =>
                anchors.Single().Timestamp.Kind == DateTimeKind.Utc &&
                anchors.Single().Timestamp == expectedUtcTimestamp),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that handling the same command twice is idempotent and does not trigger sequence conflict errors.
    /// </summary>
    [Fact]
    public async Task Handle_Should_BeIdempotent_WhenSameCommandIsHandledTwice()
    {
        // Arrange
        var (command, request) = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };
        var createdAnchor = request.ToModel(command.MatchId);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .SetupSequence(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([createdAnchor]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([createdAnchor]);

        // Act
        var firstResult = await _handler.Handle(command, CancellationToken.None);
        var secondResult = await _handler.Handle(command, CancellationToken.None);

        // Assert
        firstResult.Should().ContainSingle().Which.Should().Be(request.Id);
        secondResult.Should().ContainSingle().Which.Should().Be(request.Id);

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<IEnumerable<TimeAnchor>>(anchors => anchors.Single().Id == request.Id),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that attempting to create an anchor with an existing ID but a different Type or PeriodNumber throws a <see cref="ConflictException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_ExistingAnchorHasDifferentParameters()
    {
        // Arrange
        var anchorId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = anchorId, MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow }
        };

        var request = new CreateTimeAnchorRequest(
            Id: anchorId,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: DateTime.UtcNow.AddMinutes(5));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"Time anchor with ID {anchorId} already exists with different parameters.");
    }

    /// <summary>
    /// Verifies that attempting to create an anchor with an existing ID but a different Timestamp throws a <see cref="ConflictException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_ExistingAnchorHasDifferentTimestamp()
    {
        // Arrange
        var anchorId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = anchorId, MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime }
        };

        var request = new CreateTimeAnchorRequest(
            Id: anchorId,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: baseTime.AddMinutes(1));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"Time anchor with ID {anchorId} already exists with different parameters.");
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the Match cannot be found.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_MatchNotFound()
    {
        // Arrange
        var (command, _) = CreateCommand(TimeAnchorType.PeriodStart);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {command.MatchId} was not found.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown if we try to start a period that is already started.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_PeriodAlreadyStarted()
    {
        // Arrange
        var (command, request) = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), PeriodNumber = request.PeriodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow }
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"Period {request.PeriodNumber} already started.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown if we try to end a period before starting it.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_EndingUnstartedPeriod()
    {
        // Arrange
        var (command, request) = CreateCommand(TimeAnchorType.PeriodEnd);
        var match = new Match { Id = command.MatchId };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"Cannot end period {request.PeriodNumber} before it starts.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown if we try to start a stoppage when the match is already stopped.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_MatchAlreadyStopped()
    {
        // Arrange
        var (command, request) = CreateCommand(TimeAnchorType.StoppageStart);
        var match = new Match { Id = command.MatchId };

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), PeriodNumber = request.PeriodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow.AddMinutes(-10) },
            new() { Id = Guid.NewGuid(), PeriodNumber = request.PeriodNumber, Type = TimeAnchorType.StoppageStart, Timestamp = DateTime.UtcNow }
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Match is already stopped.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a PostgreSQL custom business rule (P0001) is triggered.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Postgres_BusinessRule_Violation()
    {
        // Arrange
        var (command, _) = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };
        const string dbErrorMessage = "Custom DB validation error";

        var pgException = new PostgresException(dbErrorMessage, "ERROR", "ERROR", "P0001");

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>();
    }

    /// <summary>
    /// Verifies that a backdated invalid anchor (e.g. StoppageStart with timestamp prior to PeriodStart)
    /// is rejected with a ConflictException when chronological sequence rules are evaluated.
    /// </summary>
    [Fact]
    public async Task Handle_Should_RejectBackdatedInvalidAnchor_WhenSequenceIsViolated()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime }
        };

        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageStart,
            Timestamp: baseTime.AddMinutes(-5));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Stoppage can only occur during an active period.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when attempting to end a period that is already finished.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_PeriodAlreadyFinished()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime },
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodEnd, Timestamp = baseTime.AddMinutes(10) }
        };

        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: baseTime.AddMinutes(12));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"Period {request.PeriodNumber} is already finished.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when attempting to end a period while a stoppage is currently active.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_EndingPeriodWhileStoppageIsActive()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime },
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.StoppageStart, Timestamp = baseTime.AddMinutes(5) }
        };

        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: baseTime.AddMinutes(10));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Cannot end period: a stoppage is currently active.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when attempting to end a stoppage when no stoppage is active.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_EndingStoppageWithoutActiveStoppage()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime }
        };

        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageEnd,
            Timestamp: baseTime.AddMinutes(5));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Cannot end stoppage: Match was not stopped.");
    }

    /// <summary>
    /// Verifies that when multiple anchors share the exact same timestamp, 
    /// the sequence uses Id as a deterministic secondary sort key during validation.
    /// </summary>
    [Fact]
    public async Task Handle_Should_SortDeterministically_WhenAnchorsHaveEqualTimestamp()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var sameTimestamp = DateTime.UtcNow;

        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = id1, MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = sameTimestamp }
        };

        var request = new CreateTimeAnchorRequest(
            Id: id2,
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageStart,
            Timestamp: sameTimestamp);

        var command = new CreateTimeAnchorsCommand(matchId, [request]);
        var createdAnchor = request.ToModel(matchId);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([createdAnchor]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(request.Id);
        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a valid late-arriving anchor (e.g. StoppageEnd synced after a later StoppageStart was already received)
    /// is accepted when inserted into its correct chronological position in the sequence.
    /// </summary>
    [Fact]
    public async Task Handle_Should_AcceptValidLateArrivingAnchor_WhenChronologicalSequenceIsValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime },
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.StoppageStart, Timestamp = baseTime.AddMinutes(5) },
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.StoppageStart, Timestamp = baseTime.AddMinutes(8) }
        };

        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageEnd,
            Timestamp: baseTime.AddMinutes(7));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);
        var createdAnchor = request.ToModel(matchId);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([createdAnchor]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(request.Id);
        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a valid multi-anchor batch command is processed atomically and passed to UpsertAsync in a single call.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CreateMultipleAnchors_When_MultiAnchorSequenceIsValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var request1 = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: baseTime);

        var request2 = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: baseTime.AddMinutes(8));

        var command = new CreateTimeAnchorsCommand(matchId, [request1, request2]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var createdAnchors = new List<TimeAnchor> { request1.ToModel(matchId), request2.ToModel(matchId) };

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAnchors);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(2);
        resultList.Should().Contain([request1.Id, request2.Id]);

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<IEnumerable<TimeAnchor>>(anchors => anchors.Count() == 2),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that an invalid multi-candidate sequence within the batch payload fails sequence validation and prevents UpsertAsync execution.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Not_Call_UpsertAsync_When_MultiAnchorSequenceIsInvalid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        // Sequence error: PeriodEnd occurs before PeriodStart in the same incoming batch sequence
        var requestEnd = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: baseTime);

        var requestStart = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: baseTime.AddMinutes(10));

        var command = new CreateTimeAnchorsCommand(matchId, [requestEnd, requestStart]);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Cannot end period 1 before it starts.");

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.IsAny<IEnumerable<TimeAnchor>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that sub-millisecond timestamp drift (such as database truncation) between an existing anchor 
    /// and a re-submitted request does not trigger a false positive <see cref="ConflictException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_Should_IgnoreSubMillisecondTimestampDrift_WhenParametersMatch()
    {
        // Arrange
        var anchorId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var match = new Match { Id = matchId };
        var baseTime = DateTime.UtcNow;

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = anchorId, MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime }
        };

        // Timestamp differs by 500 microseconds (0.5 ms), simulating DB precision truncation
        var request = new CreateTimeAnchorRequest(
            Id: anchorId,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: baseTime.AddTicks(5000));

        var command = new CreateTimeAnchorsCommand(matchId, [request]);
        var createdAnchor = request.ToModel(matchId);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([createdAnchor]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(anchorId);
        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<IEnumerable<TimeAnchor>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Helper method to create a valid <see cref="CreateTimeAnchorsCommand"/> and its underlying request DTO.
    /// </summary>
    /// <param name="type">The type of anchor to create.</param>
    /// <returns>A tuple containing the command and the single request item.</returns>
    private static (CreateTimeAnchorsCommand Command, CreateTimeAnchorRequest Request) CreateCommand(TimeAnchorType type)
    {
        var matchId = Guid.NewGuid();
        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: type,
            Timestamp: DateTime.UtcNow);

        var command = new CreateTimeAnchorsCommand(matchId, [request]);
        return (command, request);
    }
}