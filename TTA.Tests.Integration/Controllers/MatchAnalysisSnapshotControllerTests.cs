using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// End-to-end integration tests for <c>GET api/matches/{matchId}/teams/{teamId}/analysis-snapshot</c>.
/// </summary>
public class MatchAnalysisSnapshotControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/matches";

    /// <summary>
    /// Verifies the full snapshot is returned anonymously and contains only domain data.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldReturnOkWithFullSnapshot_WhenTeamParticipates()
    {
        // Arrange
        TestAuthHandler.IsEnabled = false;
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory, homeLineupCount: 2);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{seeded.MatchId}/teams/{seeded.HomeTeamId}/analysis-snapshot");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MatchAnalysisSnapshotResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Match.Id.Should().Be(seeded.MatchId);
        result.Teams.Home.Id.Should().Be(seeded.HomeTeamId);
        result.Teams.Guest.Id.Should().Be(seeded.GuestTeamId);
        result.Teams.Tracked.Id.Should().Be(seeded.HomeTeamId);
        result.Teams.Opponent.Id.Should().Be(seeded.GuestTeamId);
        result.Sport.Id.Should().Be(seeded.SportId);
        result.Sport.ShortName.Should().Be("SNP");
        result.SportConfiguration.Id.Should().Be(seeded.TournamentConfigurationId);
        result.SportConfiguration.Playground.Should().Be("<svg>playground</svg>");
        result.SportConfiguration.ShotMap.Should().Be("<svg>shotmap</svg>");
        result.Lineup.Should().HaveCount(2);
        result.Lineup.Select(l => l.PositionShortName).Should().Equal("CF", "D");
        result.EventDefinitions.Select(e => e.Id).Should().BeEquivalentTo(seeded.SystemEventDefinitionIds);

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainAny("promptCode", "capColorHint", "isGoalkeeper");
    }

    /// <summary>
    /// Verifies the opponent is resolved correctly when the guest team is tracked.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldTrackGuestTeam_WhenGuestTeamIdRequested()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{seeded.MatchId}/teams/{seeded.GuestTeamId}/analysis-snapshot");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MatchAnalysisSnapshotResponse>(JsonOptions);
        result!.Teams.Tracked.Id.Should().Be(seeded.GuestTeamId);
        result.Teams.Opponent.Id.Should().Be(seeded.HomeTeamId);
        result.Lineup.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies an empty lineup yields HTTP 200 with an empty lineup collection (not 404).
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldReturnOkWithEmptyLineup_WhenTeamHasNoLineupEntries()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory, homeLineupCount: 0);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{seeded.MatchId}/teams/{seeded.HomeTeamId}/analysis-snapshot");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MatchAnalysisSnapshotResponse>(JsonOptions);
        result!.Lineup.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>
    /// Verifies HTTP 404 is returned when the match does not exist.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldReturnNotFound_WhenMatchDoesNotExist()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/teams/{seeded.HomeTeamId}/analysis-snapshot");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies HTTP 404 is returned when the team does not participate in the match.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldReturnNotFound_WhenTeamIsNotParticipant()
    {
        // Arrange
        var seeded = await MatchAnalysisSnapshotSeeder.SeedAsync(Fixture.ConnectionFactory);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{seeded.MatchId}/teams/{seeded.ExternalTeamId}/analysis-snapshot");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies HTTP 400 is returned when an empty team identifier is supplied.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldReturnBadRequest_WhenTeamIdIsEmpty()
    {
        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/teams/{Guid.Empty}/analysis-snapshot");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
