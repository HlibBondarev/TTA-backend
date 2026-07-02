using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.PlayerPresences.Handlers;
using TTA.BusinessLogic.Features.PlayerPresences.Queries;
using TTA.BusinessLogic.Services.Api;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetPlayersTimeInMatchQueryHandler"/> class.
/// Validates database projection aggregation, analytical mathematical equations, and mapping logic.
/// </summary>
public class GetPlayersTimeInMatchQueryHandlerTests
{
    private readonly Mock<IPlayerPresenceRepository> _playerPresenceRepositoryMock;
    private readonly Mock<ITimeNormalizationService> _timeNormalizationServiceMock;
    private readonly Mock<ILogger<GetPlayersTimeInMatchQueryHandler>> _loggerMock;
    private readonly GetPlayersTimeInMatchQueryHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPlayersTimeInMatchQueryHandlerTests"/> class.
    /// Sets up required mock instances and instantiates the target system handler under test.
    /// </summary>
    public GetPlayersTimeInMatchQueryHandlerTests()
    {
        _playerPresenceRepositoryMock = new Mock<IPlayerPresenceRepository>();
        _timeNormalizationServiceMock = new Mock<ITimeNormalizationService>();
        _loggerMock = new Mock<ILogger<GetPlayersTimeInMatchQueryHandler>>();

        _handler = new GetPlayersTimeInMatchQueryHandler(
            _playerPresenceRepositoryMock.Object,
            _timeNormalizationServiceMock.Object,
            _loggerMock.Object
        );
    }

    /// <summary>
    /// Verifies that the handler yields an empty collection cleanly when the repository layer returns no matching rows.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnEmptyCollection_When_NoProjectionsFound()
    {
        // Arrange
        var query = new GetPlayersTimeInMatchQuery(Guid.NewGuid(), Guid.NewGuid());

        _playerPresenceRepositoryMock
            .Setup(r => r.GetPlayersDirtyTimeByPeriodAsync(query.MatchId, query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<PlayersDirtyTimeByPeriodProjection>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();

        _timeNormalizationServiceMock
            .Verify(s => s.GetNormalizedTimeCoefficientAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler correctly groups rows by player, requests period coefficients, 
    /// processes the time transformation equation, and accurately sums metrics across multiple periods.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CalculateCorrectAggregatedTimes_When_ProjectionsExist()
    {
        // Arrange
        var query = new GetPlayersTimeInMatchQuery(Guid.NewGuid(), Guid.NewGuid());
        var playerLineupIdA = Guid.NewGuid();
        var playerLineupIdB = Guid.NewGuid();

        // Projections setup: 
        // Player A played in Period 1 (300s) and Period 2 (200s)
        // Player B played only in Period 1 (150s)
        var databaseProjections = new List<PlayersDirtyTimeByPeriodProjection>
        {
            new(playerLineupIdA, 1, 300.0),
            new(playerLineupIdA, 2, 200.0),
            new(playerLineupIdB, 1, 150.0)
        };

        _playerPresenceRepositoryMock
            .Setup(r => r.GetPlayersDirtyTimeByPeriodAsync(query.MatchId, query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(databaseProjections);

        // Scale coefficients setup: Period 1 K = 0.8, Period 2 K = 1.2
        _timeNormalizationServiceMock
            .Setup(s => s.GetNormalizedTimeCoefficientAsync(query.MatchId, 1))
            .ReturnsAsync(0.8);

        _timeNormalizationServiceMock
            .Setup(s => s.GetNormalizedTimeCoefficientAsync(query.MatchId, 2))
            .ReturnsAsync(1.2);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(2);

        // Verify Player A Calculations:
        // Dirty time: 300 + 200 = 500 seconds
        // Clean time: (300 * 0.8) + (200 * 1.2) = 240 + 240 = 480 seconds
        var playerResultA = result.SingleOrDefault(r => r.MatchLineupId == playerLineupIdA);
        playerResultA.Should().NotBeNull();
        playerResultA!.DirtyTimeInMatch.Should().Be(TimeSpan.FromSeconds(500));
        playerResultA.CleanTimeInMatch.Should().Be(TimeSpan.FromSeconds(480));

        // Verify Player B Calculations:
        // Dirty time: 150 seconds
        // Clean time: 150 * 0.8 = 120 seconds
        var playerResultB = result.SingleOrDefault(r => r.MatchLineupId == playerLineupIdB);
        playerResultB.Should().NotBeNull();
        playerResultB!.DirtyTimeInMatch.Should().Be(TimeSpan.FromSeconds(150));
        playerResultB.CleanTimeInMatch.Should().Be(TimeSpan.FromSeconds(120));

        // Verify service optimization: coefficient methods are hit exactly once per distinct period index
        _timeNormalizationServiceMock.Verify(s => s.GetNormalizedTimeCoefficientAsync(query.MatchId, 1), Times.Once);
        _timeNormalizationServiceMock.Verify(s => s.GetNormalizedTimeCoefficientAsync(query.MatchId, 2), Times.Once);
    }
}