using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.DataAccess.Repository.Api;

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

        var rawEvents = new List<object>
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
            .ReturnsAsync(new List<object>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _gameEventRepositoryMock.Verify(r => r.GetMatchEventsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Helper method to create a dynamic raw event object using ExpandoObject.
    /// Matches the property naming expected by the handler.
    /// </summary>
    /// <param name="id">The event ID.</param>
    /// <param name="name">The event name.</param>
    /// <param name="period">The match period.</param>
    /// <returns>A dynamic object with populated event data.</returns>
    private static dynamic CreateRawEvent(Guid id, string name, int period)
    {
        dynamic e = new ExpandoObject();
        e.id = id;
        e.matchlineupid = Guid.NewGuid();
        e.eventdefinitionid = Guid.NewGuid();
        e.eventname = name;
        e.ispositive = true;
        e.periodnumber = period;
        e.eventtimestamp = DateTime.UtcNow;
        e.normalizedmatchtime = TimeSpan.FromMinutes(20);
        e.isleadtogoal = false;
        e.playername = "Player Name";
        e.playernumber = 7;
        e.teamid = Guid.NewGuid();
        e.teamname = "Team Name";
        return e;
    }
}