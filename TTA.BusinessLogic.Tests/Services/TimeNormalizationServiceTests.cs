using FluentAssertions;
using Moq;
using TTA.BusinessLogic.Services;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Services;

/// <summary>
/// Unit tests for the <see cref="TimeNormalizationService"/> class.
/// Validates piecewise-linear timeline calculations, active segment slicing, and defensive boundary exceptions.
/// </summary>
public class TimeNormalizationServiceTests
{
    private readonly Mock<ITimeAnchorRepository> _timeAnchorRepositoryMock;
    private readonly TimeNormalizationService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeNormalizationServiceTests"/> class.
    /// Sets up the required repository mock wrapper environment.
    /// </summary>
    public TimeNormalizationServiceTests()
    {
        _timeAnchorRepositoryMock = new Mock<ITimeAnchorRepository>();
        _service = new TimeNormalizationService(_timeAnchorRepositoryMock.Object);
    }

    /// <summary>
    /// Verifies that the service calculates the correct coefficient (K) when a period flows smoothly without any stoppages.
    /// Formula verification: Nominal 8 minutes / Real 10 minutes = 0.8 coefficient.
    /// </summary>
    [Fact]
    public async Task GetNormalizedTimeCoefficientAsync_ShouldReturnCorrectCoefficient_WhenNoStoppagesExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        const int periodNumber = 1;
        const int nominalDurationMinutes = 8; // Standard Water Polo period rule
        var baseTime = DateTime.UtcNow;

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchPeriodDurationMinutesAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nominalDurationMinutes);

        var anchors = new List<TimeAnchor>
        {
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime },
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodEnd, Timestamp = baseTime.AddMinutes(10) } // Elapsed real time = 10 minutes
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(anchors);

        // Act
        var result = await _service.GetNormalizedTimeCoefficientAsync(matchId, periodNumber);

        // Assert
        // Expected K = 8 / 10 = 0.8
        result.Should().Be(0.8);
    }

    /// <summary>
    /// Verifies that the service accurately filters out dead time (stoppage segments) and computes the scale coefficient based only on active segments.
    /// Timeline: Active (0 to 4 mins = 4m) + Paused (4 to 6 mins = 2m dead) + Active (6 to 12 mins = 6m) -> Total Active Real Time = 10 minutes.
    /// Formula verification: Nominal 8 minutes / Active Real 10 minutes = 0.8 coefficient.
    /// </summary>
    [Fact]
    public async Task GetNormalizedTimeCoefficientAsync_ShouldFilterStoppages_WhenMultipleStoppagesExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        const int periodNumber = 1;
        const int nominalDurationMinutes = 8;
        var baseTime = DateTime.UtcNow;

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchPeriodDurationMinutesAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nominalDurationMinutes);

        var anchors = new List<TimeAnchor>
        {
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime },
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.StoppageStart, Timestamp = baseTime.AddMinutes(4) }, // +4 mins active
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.StoppageEnd, Timestamp = baseTime.AddMinutes(6) },   // +2 mins dead time (skipped)
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodEnd, Timestamp = baseTime.AddMinutes(12) }    // +6 mins active
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(anchors);

        // Act
        var result = await _service.GetNormalizedTimeCoefficientAsync(matchId, periodNumber);

        // Assert
        // Expected K = 8 / (4 + 6) = 8 / 10 = 0.8
        result.Should().Be(0.8);
    }

    /// <summary>
    /// Verifies that an <see cref="InvalidOperationException"/> is thrown when the nominal duration returned from the database configuration is invalid (e.g. zero or negative).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetNormalizedTimeCoefficientAsync_ShouldThrowException_WhenNominalDurationIsInvalid(int invalidDuration)
    {
        // Arrange
        var matchId = Guid.NewGuid();
        const int periodNumber = 1;

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchPeriodDurationMinutesAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidDuration);

        // Act
        var act = async () => await _service.GetNormalizedTimeCoefficientAsync(matchId, periodNumber);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Invalid or missing sport configuration*");
    }

    /// <summary>
    /// Verifies that an <see cref="InvalidOperationException"/> is thrown when there are fewer than two time anchors registered for the requested period.
    /// </summary>
    [Fact]
    public async Task GetNormalizedTimeCoefficientAsync_ShouldThrowException_WhenAnchorsAreInsufficient()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        const int periodNumber = 1;

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchPeriodDurationMinutesAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(8);

        var insufficientAnchors = new List<TimeAnchor>
        {
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = DateTime.UtcNow }
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(insufficientAnchors);

        // Act
        var act = async () => await _service.GetNormalizedTimeCoefficientAsync(matchId, periodNumber);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Insufficient time anchors recorded*");
    }

    /// <summary>
    /// Verifies that the service handles division-by-zero safely and throws an exception if the timeline evaluates to zero effective active minutes.
    /// </summary>
    [Fact]
    public async Task GetNormalizedTimeCoefficientAsync_ShouldThrowException_WhenTotalEffectiveRealDurationIsZero()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        const int periodNumber = 1;
        var baseTime = DateTime.UtcNow;

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchPeriodDurationMinutesAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(8);

        // Scenario where a period ends immediately at the exact same timestamp it started (0 seconds active)
        var zeroDurationAnchors = new List<TimeAnchor>
        {
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime },
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodEnd, Timestamp = baseTime }
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(zeroDurationAnchors);

        // Act
        var act = async () => await _service.GetNormalizedTimeCoefficientAsync(matchId, periodNumber);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*evaluated to zero*");
    }

    /// <summary>
    /// Verifies that the service ignores malformed timeline segments where an active block does not close properly.
    /// Timeline: PeriodStart (0m) -> Malformed PeriodStart (2m) -> PeriodEnd (10m).
    /// Segment 1 (0m to 2m) is ignored because it ends with PeriodStart instead of StoppageStart/PeriodEnd.
    /// Segment 2 (2m to 10m) is accumulated (8 mins active). Nominal 8 / Active 8 = 1.0 coefficient.
    /// </summary>
    [Fact]
    public async Task GetNormalizedTimeCoefficientAsync_ShouldIgnoreInvalidSegments_WhenTimelineIsMalformed()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        const int periodNumber = 1;
        const int nominalDurationMinutes = 8;
        var baseTime = DateTime.UtcNow;

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchPeriodDurationMinutesAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nominalDurationMinutes);

        var malformedAnchors = new List<TimeAnchor>
        {
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime },
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodStart, Timestamp = baseTime.AddMinutes(2) }, // Malformed duplicate start boundary
            new() { MatchId = matchId, PeriodNumber = periodNumber, Type = TimeAnchorType.PeriodEnd, Timestamp = baseTime.AddMinutes(10) }     // Valid end boundary for segment 2
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetMatchAnchorsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(malformedAnchors);

        // Act
        var result = await _service.GetNormalizedTimeCoefficientAsync(matchId, periodNumber);

        // Assert
        // Expected total active minutes = 8 (Segment 1 of 2 minutes is discarded). K = 8 / 8 = 1.0
        result.Should().Be(1.0);
    }
}