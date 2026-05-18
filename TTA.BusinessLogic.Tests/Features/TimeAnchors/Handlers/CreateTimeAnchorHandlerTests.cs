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
    /// Verifies that the handler successfully creates a time anchor when the sequence is valid.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CreateAnchor_When_SequenceIsValid()
    {
        // Arrange
        var command = CreateCommand(TimeAnchorType.PeriodStart);
        var match = new Match { Id = command.MatchId };
        var createdAnchor = new TimeAnchor { Id = Guid.NewGuid() };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        // No existing anchors for this period
        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAnchor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(createdAnchor.Id);
        _timeAnchorRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()), Times.Once);
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
            new() { PeriodNumber = command.PeriodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow }
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

        // Empty list -> PeriodStart is missing
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
            new() { PeriodNumber = command.PeriodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow.AddMinutes(-10) },
            new() { PeriodNumber = command.PeriodNumber, Type = TimeAnchorType.StoppageStart, Timestamp = DateTime.UtcNow }
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
            .ReturnsAsync(new List<TimeAnchor>());

        _timeAnchorRepositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<TimeAnchor>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>();
    }

    /// <summary>
    /// Helper method to create a valid <see cref="CreateTimeAnchorCommand"/>.
    /// </summary>
    /// <param name="type">The type of anchor to create.</param>
    /// <returns>A populated command instance.</returns>
    private static CreateTimeAnchorCommand CreateCommand(TimeAnchorType type)
    {
        return new CreateTimeAnchorCommand(
            MatchId: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: type);
    }
}
