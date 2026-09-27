using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.TimeAnchors.Handlers;
using TTA.BusinessLogic.Features.TimeAnchors.Queries;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.TimeAnchors.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetMatchAnchorsHandler"/> class.
/// Validates the mapping logic from raw repository data to a sorted collection of DTOs.
/// </summary>
public class GetMatchAnchorsHandlerTests
{
    private readonly Mock<ITimeAnchorRepository> _timeAnchorRepositoryMock;
    private readonly Mock<ILogger<GetMatchAnchorsHandler>> _loggerMock;
    private readonly GetMatchAnchorsHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetMatchAnchorsHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public GetMatchAnchorsHandlerTests()
    {
        _timeAnchorRepositoryMock = new Mock<ITimeAnchorRepository>();
        _loggerMock = new Mock<ILogger<GetMatchAnchorsHandler>>();

        _handler = new GetMatchAnchorsHandler(
            _timeAnchorRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a collection of <see cref="TimeAnchorResponse"/>
    /// when time anchors exist for the given match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnAnchors_When_AnchorsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchAnchorsQuery(matchId);

        var rawAnchors = new List<TimeAnchor>
        {
            CreateRawAnchor(Guid.NewGuid(), matchId, 1, TimeAnchorType.PeriodStart, DateTime.UtcNow.AddMinutes(-45)),
            CreateRawAnchor(Guid.NewGuid(), matchId, 1, TimeAnchorType.PeriodEnd, DateTime.UtcNow)
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawAnchors);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultList = result.ToList();
        resultList.Should().HaveCount(2);

        resultList[0].Type.Should().Be(TimeAnchorType.PeriodStart);
        resultList[0].PeriodNumber.Should().Be(1);
        resultList[0].MatchId.Should().Be(matchId);

        resultList[1].Type.Should().Be(TimeAnchorType.PeriodEnd);
        resultList[1].PeriodNumber.Should().Be(1);
        resultList[1].MatchId.Should().Be(matchId);

        _timeAnchorRepositoryMock.Verify(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns an empty collection when no time anchors are found for the match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnEmptyCollection_When_NoAnchorsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchAnchorsQuery(matchId);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _timeAnchorRepositoryMock.Verify(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns time anchors in strict chronological order,
    /// sorted primarily by their UTC Timestamp.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnAnchorsInChronologicalOrder()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchAnchorsQuery(matchId);
        var baseTimestamp = DateTime.UtcNow;

        var anchor1 = CreateRawAnchor(Guid.NewGuid(), matchId, 1, TimeAnchorType.PeriodStart, baseTimestamp.AddMinutes(-20));
        var anchor2 = CreateRawAnchor(Guid.NewGuid(), matchId, 1, TimeAnchorType.StoppageStart, baseTimestamp.AddMinutes(-15));
        var anchor3 = CreateRawAnchor(Guid.NewGuid(), matchId, 1, TimeAnchorType.StoppageEnd, baseTimestamp.AddMinutes(-10));
        var anchor4 = CreateRawAnchor(Guid.NewGuid(), matchId, 1, TimeAnchorType.PeriodEnd, baseTimestamp);

        // Provide anchors out of order to ensure the handler sorts them properly
        var rawAnchors = new List<TimeAnchor> { anchor3, anchor1, anchor4, anchor2 };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawAnchors);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(4);

        result[0].Type.Should().Be(TimeAnchorType.PeriodStart);
        result[1].Type.Should().Be(TimeAnchorType.StoppageStart);
        result[2].Type.Should().Be(TimeAnchorType.StoppageEnd);
        result[3].Type.Should().Be(TimeAnchorType.PeriodEnd);

        result[0].Timestamp.Should().BeBefore(result[1].Timestamp);
        result[1].Timestamp.Should().BeBefore(result[2].Timestamp);
        result[2].Timestamp.Should().BeBefore(result[3].Timestamp);
    }

    /// <summary>
    /// Helper method to create a TimeAnchor model object with populated data.
    /// </summary>
    /// <param name="id">The unique identifier for the time anchor.</param>
    /// <param name="matchId">The unique identifier of the associated match.</param>
    /// <param name="period">The period number.</param>
    /// <param name="type">The specific type of the time anchor.</param>
    /// <param name="timestamp">The exact UTC timestamp when the anchor occurred.</param>
    /// <returns>A populated TimeAnchor instance.</returns>
    private static TimeAnchor CreateRawAnchor(
        Guid id,
        Guid matchId,
        int period,
        TimeAnchorType type,
        DateTime timestamp)
    {
        return new TimeAnchor
        {
            Id = id,
            MatchId = matchId,
            PeriodNumber = period,
            Type = type,
            Timestamp = timestamp
        };
    }
}
