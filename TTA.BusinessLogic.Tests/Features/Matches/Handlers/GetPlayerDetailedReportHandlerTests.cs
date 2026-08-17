using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

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
    /// with sorted events when projections exist.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnDetailedReport_When_ProjectionsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var matchLineupId = Guid.NewGuid();
        var query = new GetPlayerDetailedReportQuery(matchId, matchLineupId);
        var baseTimestamp = DateTime.UtcNow;

        var projections = new List<PlayerDetailedReportProjection>
        {
            new(
                MatchLineupId: matchLineupId,
                FirstName: "Alex",
                LastName: "Johnson",
                Number: 9,
                EventId: Guid.NewGuid(),
                EventName: "Yellow Card",
                IsPositive: false,
                PeriodNumber: 2,
                EventTimestamp: baseTimestamp.AddMinutes(-5),
                NormalizedMatchTime: TimeSpan.FromMinutes(35),
                IsLeadToGoal: false
            ),
            new(
                MatchLineupId: matchLineupId,
                FirstName: "Alex",
                LastName: "Johnson",
                Number: 9,
                EventId: Guid.NewGuid(),
                EventName: "Goal",
                IsPositive: true,
                PeriodNumber: 1,
                EventTimestamp: baseTimestamp.AddMinutes(-20),
                NormalizedMatchTime: TimeSpan.FromMinutes(12),
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

        // Events should be sorted chronologically by NormalizedMatchTime
        eventsList[0].EventName.Should().Be("Goal");
        eventsList[0].NormalizedMatchTime.Should().Be(TimeSpan.FromMinutes(12));

        eventsList[1].EventName.Should().Be("Yellow Card");
        eventsList[1].NormalizedMatchTime.Should().Be(TimeSpan.FromMinutes(35));

        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns a detailed report with an empty events list
    /// when the player lineup entry exists but has no recorded events.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnEmptyEvents_When_PlayerHasNoEvents()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var matchLineupId = Guid.NewGuid();
        var query = new GetPlayerDetailedReportQuery(matchId, matchLineupId);

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

        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()), Times.Once);
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

        _matchRepositoryMock
            .Setup(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match lineup with ID {matchLineupId} not found.");

        _matchRepositoryMock.Verify(r => r.GetPlayerDetailedReportAsync(matchId, matchLineupId, It.IsAny<CancellationToken>()), Times.Once);
    }
}