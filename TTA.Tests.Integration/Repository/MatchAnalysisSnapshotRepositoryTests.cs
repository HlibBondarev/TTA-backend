using FluentAssertions;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="MatchRepository.GetMatchAnalysisSnapshotAsync"/> and the underlying
/// PostgreSQL routines, executed against a real database container.
/// </summary>
public class MatchAnalysisSnapshotRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly MatchRepository _repository = new(fixture.ConnectionFactory);

    /// <summary>
    /// Verifies the match, teams and sport are returned for a participating tracked team.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshotAsync_ShouldReturnContext_WhenTeamParticipates()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var result = await _repository.GetMatchAnalysisSnapshotAsync(seeded.MatchId, seeded.GuestTeamId);

        // Assert
        result.Should().NotBeNull();
        var context = result!.Context;
        context.MatchId.Should().Be(seeded.MatchId);
        context.HomeTeamId.Should().Be(seeded.HomeTeamId);
        context.HomeTeamName.Should().Be("Home FC");
        context.GuestTeamId.Should().Be(seeded.GuestTeamId);
        context.GuestTeamName.Should().Be("Guest FC");
        context.MatchNumber.Should().Be("M-SNAP");
        context.Venue.Should().Be("Main Pool");
        context.SportId.Should().Be(seeded.SportId);
        context.SportShortName.Should().Be("SNP");
    }

    /// <summary>
    /// Verifies the tournament configuration (not the sport default) is returned with SVG fields and all parameters.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshotAsync_ShouldReturnTournamentConfiguration_NotSportDefault()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var result = await _repository.GetMatchAnalysisSnapshotAsync(seeded.MatchId, seeded.HomeTeamId);

        // Assert
        var context = result!.Context;
        context.ConfigurationId.Should().Be(seeded.TournamentConfigurationId).And.NotBe(seeded.DefaultConfigurationId);
        context.UsesCleanTime.Should().BeTrue();
        context.PeriodsCount.Should().Be(4);
        context.PeriodDurationMinutes.Should().Be(8);
        context.FieldSize.Should().Be("25x20 sq.m.");
        context.RosterLimit.Should().Be(15);
        context.LineupLimit.Should().Be(15);
        context.ActivePlayersLimit.Should().Be(7);
        context.FieldLength.Should().Be(25m);
        context.FieldWidth.Should().Be(20m);
        context.MaxPersonalFouls.Should().Be(3);
        context.Playground.Should().Be("<svg>playground</svg>");
        context.ShotMap.Should().Be("<svg>shotmap</svg>");
    }

    /// <summary>
    /// Verifies only the tracked team lineup is returned, with position details including the short name.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshotAsync_ShouldReturnTrackedTeamLineupOnly_WithPositionDetails()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory, homeLineupCount: 2);

        // Act
        var home = await _repository.GetMatchAnalysisSnapshotAsync(seeded.MatchId, seeded.HomeTeamId);
        var guest = await _repository.GetMatchAnalysisSnapshotAsync(seeded.MatchId, seeded.GuestTeamId);

        // Assert
        home!.Lineup.Should().HaveCount(2);
        home.Lineup.Select(l => l.Number).Should().Equal(1, 2);
        home.Lineup[0].PositionName.Should().Be("Center Forward");
        home.Lineup[0].PositionShortName.Should().Be("CF");
        home.Lineup[0].PositionId.Should().NotBeEmpty();
        home.Lineup[1].PositionShortName.Should().Be("D");
        guest!.Lineup.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies an empty lineup is returned as an empty collection (not null) for a participating team.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshotAsync_ShouldReturnEmptyLineup_WhenTeamHasNoLineupEntries()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory, homeLineupCount: 0);

        // Act
        var result = await _repository.GetMatchAnalysisSnapshotAsync(seeded.MatchId, seeded.HomeTeamId);

        // Assert
        result.Should().NotBeNull();
        result!.Lineup.Should().NotBeNull().And.BeEmpty();
        result.EventDefinitions.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies only active system-level event definitions are returned (custom and soft-deleted are excluded).
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshotAsync_ShouldReturnOnlyActiveSystemEventDefinitions()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var result = await _repository.GetMatchAnalysisSnapshotAsync(seeded.MatchId, seeded.HomeTeamId);

        // Assert
        var ids = result!.EventDefinitions.Select(e => e.Id).ToList();
        ids.Should().BeEquivalentTo(seeded.SystemEventDefinitionIds);
        ids.Should().NotIntersectWith(seeded.ExcludedEventDefinitionIds);

        var exclusion = result.EventDefinitions.Single(e => e.ShortName == "E");
        exclusion.Name.Should().Be("Exclusion");
        exclusion.IsPositive.Should().BeFalse();
        exclusion.IsShot.Should().BeFalse();
        exclusion.Score.Should().Be(0);
        exclusion.FoulImpact.Should().Be(1);
        exclusion.PenaltyDurationSeconds.Should().Be(18);

        var goal = result.EventDefinitions.Single(e => e.ShortName == "G");
        goal.IsPositive.Should().BeTrue();
        goal.IsShot.Should().BeTrue();
        goal.Score.Should().Be(1);
        goal.PenaltyDurationSeconds.Should().BeNull();
    }

    /// <summary>
    /// Verifies null is returned when the match does not exist.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshotAsync_ShouldReturnNull_WhenMatchDoesNotExist()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var result = await _repository.GetMatchAnalysisSnapshotAsync(Guid.NewGuid(), seeded.HomeTeamId);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies null is returned when the team does not participate in the match.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshotAsync_ShouldReturnNull_WhenTeamIsNotParticipant()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var result = await _repository.GetMatchAnalysisSnapshotAsync(seeded.MatchId, seeded.ExternalTeamId);

        // Assert
        result.Should().BeNull();
    }
}
