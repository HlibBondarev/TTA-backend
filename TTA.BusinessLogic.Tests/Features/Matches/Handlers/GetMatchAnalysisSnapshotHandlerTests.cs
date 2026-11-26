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
/// Unit tests for <see cref="GetMatchAnalysisSnapshotHandler"/> verifying projection mapping,
/// tracked/opponent team resolution, empty lineup handling and not-found behavior.
/// </summary>
public class GetMatchAnalysisSnapshotHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly GetMatchAnalysisSnapshotHandler _handler;

    private readonly Guid _matchId = Guid.NewGuid();
    private readonly Guid _homeTeamId = Guid.NewGuid();
    private readonly Guid _guestTeamId = Guid.NewGuid();

    public GetMatchAnalysisSnapshotHandlerTests()
    {
        _handler = new GetMatchAnalysisSnapshotHandler(
            _matchRepositoryMock.Object,
            Mock.Of<ILogger<GetMatchAnalysisSnapshotHandler>>());
    }

    /// <summary>
    /// Verifies that all projection data is mapped into the response when the tracked team is the home team.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedSnapshot_WhenTrackedTeamIsHome()
    {
        // Arrange
        var projection = BuildProjection(lineupCount: 2, eventDefinitionCount: 3);
        SetupRepository(_homeTeamId, projection);

        // Act
        var result = await _handler.Handle(new GetMatchAnalysisSnapshotQuery(_matchId, _homeTeamId), CancellationToken.None);

        // Assert
        result.Match.Id.Should().Be(_matchId);
        result.Match.TournamentName.Should().Be("Summer League");
        result.Match.MatchNumber.Should().Be("M-1");

        result.Teams.Home.Id.Should().Be(_homeTeamId);
        result.Teams.Guest.Id.Should().Be(_guestTeamId);
        result.Teams.Tracked.Id.Should().Be(_homeTeamId);
        result.Teams.Opponent.Id.Should().Be(_guestTeamId);
        result.Teams.Opponent.Name.Should().Be("Guest FC");

        result.Sport.Should().Be(new TTA.BusinessLogic.Features.Matches.DTOs.MatchAnalysisSportResponse(
            projection.Context.SportId, "Water Polo", "WP"));

        var config = result.SportConfiguration;
        config.Id.Should().Be(projection.Context.ConfigurationId);
        config.RosterLimit.Should().Be(15);
        config.LineupLimit.Should().Be(15);
        config.ActivePlayersLimit.Should().Be(7);
        config.FieldLength.Should().Be(25m);
        config.FieldWidth.Should().Be(20m);
        config.MaxPersonalFouls.Should().Be(3);
        config.Playground.Should().Be("<svg>playground</svg>");
        config.ShotMap.Should().Be("<svg>shotmap</svg>");

        result.Lineup.Should().HaveCount(2);
        result.Lineup[0].PositionShortName.Should().Be("CF");
        result.Lineup[0].PositionName.Should().Be("Center Forward");
        result.EventDefinitions.Should().HaveCount(3);
        result.EventDefinitions[0].ShortName.Should().Be("EV0");
        result.EventDefinitions[0].FoulImpact.Should().Be(1);
        result.EventDefinitions[0].PenaltyDurationSeconds.Should().Be(18);
    }

    /// <summary>
    /// Verifies that the guest team is tracked and the home team is the opponent when the guest team is requested.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldResolveOpponentAsHome_WhenTrackedTeamIsGuest()
    {
        // Arrange
        SetupRepository(_guestTeamId, BuildProjection(lineupCount: 1, eventDefinitionCount: 1));

        // Act
        var result = await _handler.Handle(new GetMatchAnalysisSnapshotQuery(_matchId, _guestTeamId), CancellationToken.None);

        // Assert
        result.Teams.Tracked.Id.Should().Be(_guestTeamId);
        result.Teams.Opponent.Id.Should().Be(_homeTeamId);
    }

    /// <summary>
    /// Verifies that an empty lineup is returned as an empty collection and not treated as an error.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnEmptyLineup_WhenTrackedTeamHasNoLineupEntries()
    {
        // Arrange
        SetupRepository(_homeTeamId, BuildProjection(lineupCount: 0, eventDefinitionCount: 2));

        // Act
        var result = await _handler.Handle(new GetMatchAnalysisSnapshotQuery(_matchId, _homeTeamId), CancellationToken.None);

        // Assert
        result.Lineup.Should().NotBeNull().And.BeEmpty();
        result.EventDefinitions.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that a nullable shot map and personal fouls limit are passed through as null.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldKeepNullableConfigurationFieldsNull_WhenNotConfigured()
    {
        // Arrange
        var projection = BuildProjection(0, 0);
        projection = projection with { Context = projection.Context with { ShotMap = null, MaxPersonalFouls = null } };
        SetupRepository(_homeTeamId, projection);

        // Act
        var result = await _handler.Handle(new GetMatchAnalysisSnapshotQuery(_matchId, _homeTeamId), CancellationToken.None);

        // Assert
        result.SportConfiguration.ShotMap.Should().BeNull();
        result.SportConfiguration.MaxPersonalFouls.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when the repository returns no snapshot
    /// (missing match or non-participating team).
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenSnapshotIsNull()
    {
        // Arrange
        _matchRepositoryMock
            .Setup(r => r.GetMatchAnalysisSnapshotAsync(_matchId, _homeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchAnalysisSnapshotProjection?)null);

        // Act
        var act = async () => await _handler.Handle(new GetMatchAnalysisSnapshotQuery(_matchId, _homeTeamId), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>
    /// Verifies that the cancellation token is propagated to the repository.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPropagateCancellationToken_ToRepository()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _matchRepositoryMock
            .Setup(r => r.GetMatchAnalysisSnapshotAsync(_matchId, _homeTeamId, cts.Token))
            .ReturnsAsync(BuildProjection(0, 0));

        // Act
        await _handler.Handle(new GetMatchAnalysisSnapshotQuery(_matchId, _homeTeamId), cts.Token);

        // Assert
        _matchRepositoryMock.Verify(r => r.GetMatchAnalysisSnapshotAsync(_matchId, _homeTeamId, cts.Token), Times.Once);
    }

    private void SetupRepository(Guid teamId, MatchAnalysisSnapshotProjection projection) =>
        _matchRepositoryMock
            .Setup(r => r.GetMatchAnalysisSnapshotAsync(_matchId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

    private MatchAnalysisSnapshotProjection BuildProjection(int lineupCount, int eventDefinitionCount)
    {
        var context = new MatchAnalysisContextProjection(
            _matchId, Guid.NewGuid(), "Summer League", DateTime.UtcNow, "M-1", "Main Pool", 24.5, 5, 4,
            _homeTeamId, "Home FC", _guestTeamId, "Guest FC",
            Guid.NewGuid(), "Water Polo", "WP",
            Guid.NewGuid(), true, 4, 8, "25x20 sq.m.", 15, 15, 7,
            "<svg>playground</svg>", "<svg>shotmap</svg>", 25m, 20m, 3);

        var lineup = Enumerable.Range(0, lineupCount)
            .Select(i => new MatchAnalysisLineupProjection(
                Guid.NewGuid(), Guid.NewGuid(), $"First{i}", $"Last{i}", i + 1, Guid.NewGuid(), "Center Forward", "CF"))
            .ToList();

        var definitions = Enumerable.Range(0, eventDefinitionCount)
            .Select(i => new MatchAnalysisEventDefinitionProjection(
                Guid.NewGuid(), $"Event {i}", $"EV{i}", i % 2 == 0, true, 1, 1, 18))
            .ToList();

        return new MatchAnalysisSnapshotProjection(context, lineup, definitions);
    }
}
