using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetMatchEventsTimelineQueryHandler"/> class.
/// Validates the mapping logic from raw repository data to a collection of DTOs.
/// </summary>
public class GetMatchEventsTimelineQueryHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<ILogger<GetMatchEventsTimelineQueryHandler>> _loggerMock;
    private readonly GetMatchEventsTimelineQueryHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetMatchEventsTimelineQueryHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public GetMatchEventsTimelineQueryHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _loggerMock = new Mock<ILogger<GetMatchEventsTimelineQueryHandler>>();

        _handler = new GetMatchEventsTimelineQueryHandler(
            _gameEventRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a collection of <see cref="GameEventResponse"/>
    /// when events exist for the given match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnTimeline_When_EventsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchEventsTimelineQuery(matchId);

        var rawEvents = new List<GameEventProjection>
        {
            CreateRawEvent(Guid.NewGuid(), "Goal", 1),
            CreateRawEvent(Guid.NewGuid(), "Yellow Card", 2)
        };

        _gameEventRepositoryMock
            .Setup(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawEvents);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultList = result.ToList();
        resultList.Should().HaveCount(2);

        resultList[0].EventName.Should().Be("Goal");
        resultList[0].PeriodNumber.Should().Be(1);

        resultList[1].EventName.Should().Be("Yellow Card");
        resultList[1].PeriodNumber.Should().Be(2);

        _gameEventRepositoryMock.Verify(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns an empty collection when no events are found for the match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnEmptyCollection_When_NoEventsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchEventsTimelineQuery(matchId);

        _gameEventRepositoryMock
            .Setup(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _gameEventRepositoryMock.Verify(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns events in chronological order,
    /// sorted primarily by NormalizedMatchTime and secondarily by EventTimestamp.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnEventsInChronologicalOrder()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchEventsTimelineQuery(matchId);

        var event1 = CreateRawEvent(Guid.NewGuid(), "Early Event", 1, DateTime.UtcNow.AddMinutes(-20), TimeSpan.FromMinutes(10));
        var event2 = CreateRawEvent(Guid.NewGuid(), "Late Event", 2, DateTime.UtcNow.AddMinutes(-5), TimeSpan.FromMinutes(40));
        var event3 = CreateRawEvent(Guid.NewGuid(), "Middle Event", 1, DateTime.UtcNow.AddMinutes(-15), TimeSpan.FromMinutes(25));

        // We return them out of order from the repository
        var rawEvents = new List<GameEventProjection> { event2, event1, event3 };

        _gameEventRepositoryMock
            .Setup(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawEvents);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].EventName.Should().Be("Early Event");
        result[1].EventName.Should().Be("Middle Event");
        result[2].EventName.Should().Be("Late Event");

        result[0].NormalizedMatchTime.Should().NotBeNull();
        result[1].NormalizedMatchTime.Should().NotBeNull();
        result[2].NormalizedMatchTime.Should().NotBeNull();
        result[0].NormalizedMatchTime!.Value.Should().BeLessThanOrEqualTo(result[1].NormalizedMatchTime!.Value);
        result[1].NormalizedMatchTime!.Value.Should().BeLessThanOrEqualTo(result[2].NormalizedMatchTime!.Value);
    }

    /// <summary>
    /// Verifies that events with identical NormalizedMatchTime are sorted by EventTimestamp (tie-breaker).
    /// </summary>
    [Fact]
    public async Task Handle_ShouldSortByTimestamp_WhenNormalizedMatchTimeIsIdentical()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchEventsTimelineQuery(matchId);
        var sharedTime = TimeSpan.FromMinutes(20);
        var baseTimestamp = DateTime.UtcNow;

        // Create two events with identical match minute but different actual timestamps
        var event1 = CreateRawEvent(Guid.NewGuid(), "Yellow Card", 1);
        event1 = event1 with { NormalizedMatchTime = sharedTime, EventTimestamp = baseTimestamp.AddSeconds(30) };

        var event2 = CreateRawEvent(Guid.NewGuid(), "Goal", 1);
        event2 = event2 with { NormalizedMatchTime = sharedTime, EventTimestamp = baseTimestamp };

        var rawEvents = new List<GameEventProjection> { event1, event2 };

        _gameEventRepositoryMock
            .Setup(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawEvents);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(2);
        // Event 2 should be first because its EventTimestamp is earlier
        result[0].EventName.Should().Be("Goal");
        result[1].EventName.Should().Be("Yellow Card");
        result[0].EventTimestamp.Should().BeBefore(result[1].EventTimestamp);
    }

    /// <summary>
    /// Verifies that events with null NormalizedMatchTime are placed at the end of the timeline
    /// and are internally sorted by their EventTimestamp.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPlaceEventsWithNullNormalizedMatchTimeAtEnd()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchEventsTimelineQuery(matchId);
        var baseTimestamp = DateTime.UtcNow;

        // Valid time event
        var timedEvent = CreateRawEvent(Guid.NewGuid(), "Timed Event", 1, baseTimestamp.AddMinutes(5), TimeSpan.FromMinutes(10));

        // Explicit null-time events
        var nullTimeEvent1 = CreateRawEvent(Guid.NewGuid(), "Pre-Match Event", 1, baseTimestamp, null);
        var nullTimeEvent2 = CreateRawEvent(Guid.NewGuid(), "Another Null Time Event", 1, baseTimestamp.AddSeconds(30), null);

        var rawEvents = new List<GameEventProjection> { nullTimeEvent2, timedEvent, nullTimeEvent1 };

        _gameEventRepositoryMock
            .Setup(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawEvents);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(3);

        // Assert sorting: Timed first, then nulls sorted by timestamp
        result[0].EventName.Should().Be("Timed Event");
        result[1].EventName.Should().Be("Pre-Match Event");
        result[2].EventName.Should().Be("Another Null Time Event");

        result[1].NormalizedMatchTime.Should().BeNull(); // Now this will pass
        result[2].NormalizedMatchTime.Should().BeNull();
    }

    /// <summary>
    /// Helper method to create a dynamic raw event object using ExpandoObject.
    /// Matches the property naming expected by the handler.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="name">The event name.</param>
    /// <param name="period">The match period.</param>
    /// <returns>A GameEventProjection object with populated event data.</returns>
    private static GameEventProjection CreateRawEvent(
        Guid id,
        string name,
        int period,
        DateTime? eventTimestamp = null,
        TimeSpan? normalizedMatchTime = null) // Changed to nullable
    {
        return new GameEventProjection
        (
            Id: id,
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            EventName: name,
            IsPositive: true,
            PeriodNumber: period,
            EventTimestamp: eventTimestamp ?? DateTime.UtcNow,
            // Logic: If it's NULL, we leave it NULL. 
            // If we need a default for OTHER tests, we should be explicit there or use a sentinel.
            NormalizedMatchTime: normalizedMatchTime,
            IsLeadToGoal: false,
            PlayerName: "Player Name",
            PlayerNumber: 7,
            TeamId: Guid.NewGuid(),
            TeamName: "Team Name"
        );
    }
}