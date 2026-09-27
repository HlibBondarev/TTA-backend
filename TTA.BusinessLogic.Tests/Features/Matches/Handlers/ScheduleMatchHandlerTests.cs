using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using System.Reflection;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="ScheduleMatchHandler"/> verifying business logic 
/// for match scheduling, date validation, and error handling.
/// </summary>
public class ScheduleMatchHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ITournamentRepository> _tournamentRepositoryMock;
    private readonly Mock<ILogger<ScheduleMatchHandler>> _loggerMock;
    private readonly ScheduleMatchHandler _handler;

    public ScheduleMatchHandlerTests()
    {
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _tournamentRepositoryMock = new Mock<ITournamentRepository>();
        _loggerMock = new Mock<ILogger<ScheduleMatchHandler>>();

        _handler = new ScheduleMatchHandler(
            _matchRepositoryMock.Object,
            _tournamentRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a valid match creation command returns the expected match ID.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMatchId_WhenCommandIsValid()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var tournament = new Tournament
        {
            Id = tournamentId,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1)
        };

        var command = CreateCommand(tournamentId, DateTime.UtcNow);
        var expectedMatch = new Match { Id = Guid.NewGuid(), MatchNumber = "M-001" };

        _tournamentRepositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tournament);

        _matchRepositoryMock
            .Setup(r => r.UpsertMatchAsync(It.IsAny<Match>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMatch);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(expectedMatch.Id);
        _matchRepositoryMock.Verify(r => r.UpsertMatchAsync(It.IsAny<Match>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when the tournament does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenTournamentDoesNotExist()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var command = CreateCommand(tournamentId, DateTime.UtcNow);

        _tournamentRepositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Tournament with ID {tournamentId} was not found.");
    }

    /// <summary>
    /// Verifies that <see cref="ConflictException"/> is thrown when the match date 
    /// is outside the tournament's active date range.
    /// </summary>
    [Theory]
    [InlineData(-10)] // 10 days before tournament start
    [InlineData(10)]  // 10 days after tournament end
    public async Task Handle_ShouldThrowConflictException_WhenScheduledAtIsOutsideRange(int daysOffset)
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var tournament = new Tournament
        {
            Id = tournamentId,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1)
        };

        var invalidDate = DateTime.UtcNow.AddDays(daysOffset);
        var command = CreateCommand(tournamentId, invalidDate);

        _tournamentRepositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tournament);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Scheduled date must be within the tournament's active dates.");
    }

    /// <summary>
    /// Verifies that <see cref="ConflictException"/> is thrown when the repository 
    /// returns a P0001 error (team not registered for tournament).
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenTeamsAreNotRegistered()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var tournament = new Tournament
        {
            Id = tournamentId,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1)
        };

        var command = CreateCommand(tournamentId, DateTime.UtcNow);

        _tournamentRepositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tournament);

        // Modern way to mock PostgresException without FormatterServices
        var exception = CreatePostgresException("P0001");

        _matchRepositoryMock
            .Setup(r => r.UpsertMatchAsync(It.IsAny<Match>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Home team or guest team is not registered for this tournament");
    }

    #region Helpers

    /// <summary>
    /// Creates a default <see cref="ScheduleMatchCommand"/> for testing.
    /// </summary>
    private static ScheduleMatchCommand CreateCommand(Guid tournamentId, DateTime scheduledAt)
    {
        return new ScheduleMatchCommand(
            TournamentId: tournamentId,
            HomeTeamId: Guid.NewGuid(),
            GuestTeamId: Guid.NewGuid(),
            ScheduledAt: scheduledAt,
            MatchNumber: "M-123",
            Venue: "Arena Central"
        );
    }

    /// <summary>
    /// Helper to instantiate PostgresException via reflection, 
    /// avoiding obsolete FormatterServices.
    /// </summary>
    private static PostgresException CreatePostgresException(string sqlState)
    {
        // PostgresException has a non-public constructor. 
        // We use Activator.CreateInstance with nonPublic: true.
        var exception = (PostgresException)Activator.CreateInstance(
            typeof(PostgresException),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new object?[] { "Database error", "Severity", "InvariantSeverity", sqlState },
            null)!;

        return exception;
    }

    #endregion
}