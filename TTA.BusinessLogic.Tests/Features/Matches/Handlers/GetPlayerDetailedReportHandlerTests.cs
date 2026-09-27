using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetPlayerDetailedReportHandler"/> class.
/// Ensures correct aggregation of player detailed event projections into structured DTO responses and proper exception handling.
/// </summary>
public class GetPlayerDetailedReportHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<GetPlayerDetailedReportHandler>> _loggerMock;
    private readonly GetPlayerDetailedReportHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPlayerDetailedReportHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public GetPlayerDetailedReportHandlerTests()
    {
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<GetPlayerDetailedReportHandler>>();

        _handler = new GetPlayerDetailedReportHandler(
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a correctly structured player detailed report
    /// with events sorted strictly chronologically by EventTimestamp, even if NormalizedMatchTime suggests a different order.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnDetailedReport_When_MatchIsFinalizedAndProjectionsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var matchLineupId = Guid.NewGuid();
        var query = new GetPlayerDetailedReportQuery(matchId, matchLineupId);
        var baseTimestamp = DateTime.UtcNow;

        var finalizedMatch = new Match
        {
            Id = matchId,
            HomeScore = 4,
            GuestScore = 2
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finalizedMatch);

        var projections = new List<PlayerDetailedReportProjection>
        {
            // Event B: Period 2, happens LATER in real time, but has an EARLIER relative match time (e.g., 5th minute of 2nd period)
            new(
                MatchLineupId: matchLineupId,
                FirstName: "Alex",
                LastName: "Johnson",
                Number: 9,
                EventId: Guid.NewGuid(),
                EventName: "Yellow Card",
                IsPositive: false,
                PeriodNumber: 2,
                EventTimestamp: baseTimestamp.AddMinutes(15), // LATER real time
                NormalizedMatchTime: TimeSpan.FromMinutes(5), // EARLIER period-relative time
                IsLeadToGoal: false
            ),
            // Event A: Period 1, happens EARLIER in real time, but has a LATER relative match time (e.g., 40th minute of 1st period)
            new(
                MatchLineupId: matchLineupId,
                FirstName: "Alex",
                LastName: "Johnson",
                Number: 9,
                EventId: Guid.NewGuid(),
                EventName: "Goal",
                IsPositive: true,
                PeriodNumber: 1,
                EventTimestamp: baseTimestamp,                 // EARLIER real time
                NormalizedMatchTime: TimeSpan.FromMinutes(40), // LATER period-relative time
                IsLeadToGoal: true
            )
        };

        _matchRepositoryMock
            .Setup(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.FirstName.Should().Be("Alex");
        result.LastName.Should().Be("Johnson");
        result.Number.Should().Be(9);

        var eventsList = result.Events.ToList();
        eventsList.Should().HaveCount(2);

        // Events must be sorted chronologically by EventTimestamp
        eventsList[0].EventName.Should().Be("Goal");
        eventsList[0].EventTimestamp.Should().Be(baseTimestamp);

        eventsList[1].EventName.Should().Be("Yellow Card");
        eventsList[1].EventTimestamp.Should().Be(baseTimestamp.AddMinutes(15));

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns a detailed report with an empty events list
    /// when the player lineup entry exists in a finalized match but has no recorded events.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnEmptyEvents_When_PlayerHasNoEvents()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var matchLineupId = Guid.NewGuid();
        var query = new GetPlayerDetailedReportQuery(matchId, matchLineupId);

        var finalizedMatch = new Match
        {
            Id = matchId,
            HomeScore = 1,
            GuestScore = 0
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finalizedMatch);

        // When a player has no events, the repository returns a single projection with null event fields due to LEFT JOIN
        var projections = new List<PlayerDetailedReportProjection>
        {
            new(
                MatchLineupId: matchLineupId,
                FirstName: "Alex",
                LastName: "Johnson",
                Number: 9,
                EventId: null,
                EventName: null,
                IsPositive: null,
                PeriodNumber: null,
                EventTimestamp: null,
                NormalizedMatchTime: null,
                IsLeadToGoal: null
            )
        };

        _matchRepositoryMock
            .Setup(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.FirstName.Should().Be("Alex");
        result.Events.Should().BeEmpty();

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when the match does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ThrowNotFoundException_When_MatchDoesNotExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var matchLineupId = Guid.NewGuid();
        var query = new GetPlayerDetailedReportQuery(matchId, matchLineupId);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        Func<Task> act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {matchId} is not finalized or does not exist.");

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when the match is not finalized.
    /// </summary>
    [Theory]
    [InlineData(null, 1)]
    [InlineData(2, null)]
    [InlineData(null, null)]
    public async Task Handle_Should_ThrowNotFoundException_When_MatchIsNotFinalized(int? homeScore, int? guestScore)
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var matchLineupId = Guid.NewGuid();
        var query = new GetPlayerDetailedReportQuery(matchId, matchLineupId);

        var unfinalizedMatch = new Match
        {
            Id = matchId,
            HomeScore = homeScore,
            GuestScore = guestScore
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unfinalizedMatch);

        // Act
        Func<Task> act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {matchId} is not finalized or does not exist.");

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown 
    /// when the specified match lineup entry does not exist at all (empty projection list).
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_LineupDoesNotExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var matchLineupId = Guid.NewGuid();
        var query = new GetPlayerDetailedReportQuery(matchId, matchLineupId);

        var finalizedMatch = new Match
        {
            Id = matchId,
            HomeScore = 2,
            GuestScore = 1
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finalizedMatch);

        _matchRepositoryMock
            .Setup(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match lineup with ID {matchLineupId} not found.");

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()), Times.Once);
    }
}