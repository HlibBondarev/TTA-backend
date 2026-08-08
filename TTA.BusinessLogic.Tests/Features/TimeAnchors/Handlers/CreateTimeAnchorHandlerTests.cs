using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.TimeAnchors.Commands;
using TTA.BusinessLogic.Features.TimeAnchors.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.TimeAnchors.Handlers;

/// <summary>
/// Unit tests for the <see cref="CreateTimeAnchorHandler"/> class.
/// Ensures validation logic, state machine sequence rules, repository interaction, and exception mapping are correct.
/// </summary>
public class CreateTimeAnchorHandlerTests
{
    private readonly Mock<ITimeAnchorRepository> _timeAnchorRepositoryMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<CreateTimeAnchorHandler>> _loggerMock;
    private readonly CreateTimeAnchorHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTimeAnchorHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public CreateTimeAnchorHandlerTests()
    {
        _timeAnchorRepositoryMock = new Mock<ITimeAnchorRepository>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<CreateTimeAnchorHandler>>();

        _handler = new CreateTimeAnchorHandler(
            _timeAnchorRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully creates a time anchor using the client-supplied ID and Timestamp when the sequence is valid.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CreateAnchor_When_SequenceIsValid()
    {
        // Arrange
        var command = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };
        var createdAnchor = new TimeAnchor
        {
            Id = command.Id,
            MatchId = command.MatchId,
            PeriodNumber = command.PeriodNumber,
            Type = command.Type,
            Timestamp = command.Timestamp
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAnchor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.Id);

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<TimeAnchor>(a =>
                a.Id == command.Id &&
                a.MatchId == command.MatchId &&
                a.PeriodNumber == command.PeriodNumber &&
                a.Type == command.Type &&
                a.Timestamp == command.Timestamp),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that timestamps with different <see cref="DateTimeKind"/> values (Unspecified, Local, Utc) 
    /// are correctly normalized to UTC when mapped to the domain model during anchor creation.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task Handle_Should_NormalizeTimestampToUtc_ForAnyDateTimeKind(DateTimeKind kind)
    {
        // Arrange
        var rawTimestamp = new DateTime(2026, 8, 8, 12, 0, 0, kind);
        var command = new CreateTimeAnchorCommand(
            Id: Guid.NewGuid(),
            MatchId: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: rawTimestamp);

        var match = new Match { Id = command.MatchId };
        var expectedUtcTimestamp = kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(rawTimestamp, DateTimeKind.Utc),
            _ => rawTimestamp.ToUniversalTime()
        };

        var createdAnchor = new TimeAnchor
        {
            Id = command.Id,
            MatchId = command.MatchId,
            PeriodNumber = command.PeriodNumber,
            Type = command.Type,
            Timestamp = expectedUtcTimestamp
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAnchor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.Id);

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<TimeAnchor>(a =>
                a.Timestamp.Kind == DateTimeKind.Utc &&
                a.Timestamp == expectedUtcTimestamp),
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
        var command = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };
        var createdAnchor = new TimeAnchor
        {
            Id = command.Id,
            MatchId = command.MatchId,
            PeriodNumber = command.PeriodNumber,
            Type = command.Type,
            Timestamp = command.Timestamp
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .SetupSequence(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([createdAnchor]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAnchor);

        // Act
        var firstResult = await _handler.Handle(command, CancellationToken.None);
        var secondResult = await _handler.Handle(command, CancellationToken.None);

        // Assert
        firstResult.Should().Be(command.Id);
        secondResult.Should().Be(command.Id);

        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(
            It.Is<TimeAnchor>(a => a.Id == command.Id),
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

        var command = new CreateTimeAnchorCommand(
            Id: anchorId,
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: DateTime.UtcNow.AddMinutes(5));

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

        var command = new CreateTimeAnchorCommand(
            Id: anchorId,
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: baseTime.AddMinutes(1)); // Altered timestamp

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
        var command = CreateCommand(TimeAnchorType.PeriodStart);

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
        var command = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), PeriodNumber = command.PeriodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow }
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
            .WithMessage($"Period {command.PeriodNumber} already started.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown if we try to end a period before starting it.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_EndingUnstartedPeriod()
    {
        // Arrange
        var command = CreateCommand(TimeAnchorType.PeriodEnd);
        var match = new Match { Id = command.MatchId };

        var existingAnchors = new List<TimeAnchor>();

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
            .WithMessage($"Cannot end period {command.PeriodNumber} before it starts.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown if we try to start a stoppage when the match is already stopped.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_MatchAlreadyStopped()
    {
        // Arrange
        var command = CreateCommand(TimeAnchorType.StoppageStart);
        var match = new Match { Id = command.MatchId };

        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = Guid.NewGuid(), PeriodNumber = command.PeriodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow.AddMinutes(-10) },
            new() { Id = Guid.NewGuid(), PeriodNumber = command.PeriodNumber, Type = TimeAnchorType.StoppageStart, Timestamp = DateTime.UtcNow }
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
        var command = CreateCommand(TimeAnchorType.PeriodStart);
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
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>();
    }

    /// <summary>
    /// Verifies that a valid late-arriving anchor (e.g. StoppageEnd synced after StoppageStart) is accepted
    /// when inserted into its correct chronological position in the sequence.
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
            new() { Id = Guid.NewGuid(), MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.StoppageStart, Timestamp = baseTime.AddMinutes(5) }
        };

        // Candidate anchor: StoppageEnd arriving late with timestamp between StoppageStart and potential PeriodEnd
        var command = new CreateTimeAnchorCommand(
            Id: Guid.NewGuid(),
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageEnd,
            Timestamp: baseTime.AddMinutes(7));

        var createdAnchor = command.ToModel();

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAnchor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.Id);
        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()), Times.Once);
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

        // Candidate anchor: StoppageStart with backdated timestamp earlier than PeriodStart
        var command = new CreateTimeAnchorCommand(
            Id: Guid.NewGuid(),
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageStart,
            Timestamp: baseTime.AddMinutes(-5));

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

        var command = new CreateTimeAnchorCommand(
            Id: Guid.NewGuid(),
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: baseTime.AddMinutes(12));

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
            .WithMessage($"Period {command.PeriodNumber} is already finished.");
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

        var command = new CreateTimeAnchorCommand(
            Id: Guid.NewGuid(),
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodEnd,
            Timestamp: baseTime.AddMinutes(10));

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

        var command = new CreateTimeAnchorCommand(
            Id: Guid.NewGuid(),
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageEnd,
            Timestamp: baseTime.AddMinutes(5));

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

        // PeriodStart anchor with smaller Guid
        var existingAnchors = new List<TimeAnchor>
        {
            new() { Id = id1, MatchId = matchId, PeriodNumber = 1, Type = TimeAnchorType.PeriodStart, Timestamp = sameTimestamp }
        };

        // StoppageStart candidate with larger Guid arriving with same timestamp
        var command = new CreateTimeAnchorCommand(
            Id: id2,
            MatchId: matchId,
            PeriodNumber: 1,
            Type: TimeAnchorType.StoppageStart,
            Timestamp: sameTimestamp);

        var createdAnchor = command.ToModel();

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchors);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAnchor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.Id);
        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Helper method to create a valid <see cref="CreateTimeAnchorCommand"/> with explicit Id and Timestamp.
    /// </summary>
    /// <param name="type">The type of anchor to create.</param>
    /// <returns>A populated command instance.</returns>
    private static CreateTimeAnchorCommand CreateCommand(TimeAnchorType type)
    {
        return new CreateTimeAnchorCommand(
            Id: Guid.NewGuid(),
            MatchId: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: type,
            Timestamp: DateTime.UtcNow);
    }
}