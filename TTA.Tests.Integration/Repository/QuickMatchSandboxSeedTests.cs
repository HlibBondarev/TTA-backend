using Dapper;
using FluentAssertions;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests verifying the Waterpolo Quick Match sandbox produced by section 6 of
/// <c>03-Infrastructure-and-Seed.sql</c> and its end-to-end consumption by <c>public.create_quick_match</c>.
/// </summary>
public class QuickMatchSandboxSeedTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private const string SeedUserId = "auth0|69cf7ec5eff8f1358a0b9ae0";
    private const int RosterLimit = 15;
    private const string GoalkeeperShortName = "GK";
    private const string UtilityShortName = "UTL";

    private static readonly Guid WaterpoloSportId = Guid.Parse("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f");
    private static readonly Guid WaterpoloConfigId = Guid.Parse("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70");
    private static readonly Guid WaterpoloAltConfigId = Guid.Parse("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f71");
    private static readonly Guid HomeSquadId = Guid.Parse("22222222-2222-2222-1111-000000000001");
    private static readonly Guid OpponentSquadId = Guid.Parse("22222222-2222-2222-2222-000000000001");
    private static readonly Guid TournamentId = Guid.Parse("33333333-3333-3333-3333-000000000070");
    private static readonly Guid AltTournamentId = Guid.Parse("33333333-3333-3333-3333-000000000071");

    private sealed record SandboxTournament(Guid Id, Guid ConfigurationId);

    private sealed record QuickMatchRow(Guid TournamentId, Guid HomeTeamId, Guid GuestTeamId);

    private sealed record SeededRosterRow(Guid TournamentId, Guid TeamId, int Number, string PositionShortName);

    /// <summary>
    /// Verifies that the seed creates exactly two Waterpolo sandbox teams, two JIT tournaments (one per configuration)
    /// and 60 roster rows (15 per team and configuration).
    /// </summary>
    [Fact]
    public async Task Seed_ShouldCreateTwoTeamsTwoJitTournamentsAndSixtyRosterRows()
    {
        // Arrange
        await ReapplySeedAsync();
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // Act
        var teams = (await conn.QueryAsync<Guid>(
            "SELECT id FROM public.teams WHERE sportid = @sportId", new { sportId = WaterpoloSportId })).ToList();
        var tournaments = (await conn.QueryAsync<SandboxTournament>(
            "SELECT id AS Id, configurationid AS ConfigurationId FROM public.tournaments WHERE sportid = @sportId AND isjit = TRUE",
            new { sportId = WaterpoloSportId })).ToList();
        var rosterCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM public.playerrosters");

        // Assert
        teams.Should().BeEquivalentTo([HomeSquadId, OpponentSquadId]);
        tournaments.Should().BeEquivalentTo([new SandboxTournament(TournamentId, WaterpoloConfigId), new SandboxTournament(AltTournamentId, WaterpoloAltConfigId)]);
        rosterCount.Should().Be(2 * 2 * RosterLimit);
    }

    /// <summary>
    /// Verifies that every sandbox team has 15 roster rows per configuration, with goalkeepers strictly at caps 1 and 13
    /// and utility players at every other cap.
    /// </summary>
    /// <param name="tournamentId">The sandbox tournament identifier.</param>
    /// <param name="teamId">The sandbox team identifier.</param>
    [Theory]
    [InlineData("33333333-3333-3333-3333-000000000070", "22222222-2222-2222-1111-000000000001")]
    [InlineData("33333333-3333-3333-3333-000000000070", "22222222-2222-2222-2222-000000000001")]
    [InlineData("33333333-3333-3333-3333-000000000071", "22222222-2222-2222-1111-000000000001")]
    [InlineData("33333333-3333-3333-3333-000000000071", "22222222-2222-2222-2222-000000000001")]
    public async Task Seed_ShouldAssignGoalkeeperToCaps1And13AndUtilityElsewhere(string tournamentId, string teamId)
    {
        // Arrange
        await ReapplySeedAsync();

        // Act
        var rows = (await GetRosterRowsAsync(Guid.Parse(tournamentId), Guid.Parse(teamId))).ToList();

        // Assert
        rows.Should().HaveCount(RosterLimit);
        rows.Select(r => r.Number).Should().BeEquivalentTo(Enumerable.Range(1, RosterLimit));
        rows.Where(r => r.PositionShortName == GoalkeeperShortName).Select(r => r.Number)
            .Should().BeEquivalentTo([1, 13]);
        rows.Where(r => r.Number is not (1 or 13)).Should().OnlyContain(r => r.PositionShortName == UtilityShortName);
    }

    /// <summary>
    /// Verifies that re-running the seed script does not duplicate sandbox rows.
    /// </summary>
    [Fact]
    public async Task Seed_ShouldBeIdempotent_WhenAppliedTwice()
    {
        // Arrange
        await ReapplySeedAsync();
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        const string countsSql = @"
            SELECT (SELECT COUNT(*) FROM public.teams) + (SELECT COUNT(*) FROM public.tournaments) + (SELECT COUNT(*) FROM public.playerrosters)";
        var before = await conn.ExecuteScalarAsync<long>(countsSql);

        // Act
        await conn.ExecuteAsync(await File.ReadAllTextAsync(FindSeedScript()));

        // Assert
        var after = await conn.ExecuteScalarAsync<long>(countsSql);
        after.Should().Be(before);
    }

    /// <summary>
    /// Verifies the end-to-end flow on the alternative Waterpolo configuration:
    /// <c>create_quick_match</c> then <c>copy_team_roster_to_match_lineup</c> then <c>get_match_analysis_lineup</c>
    /// returns 2 goalkeepers and 13 utility players for each team.
    /// </summary>
    [Fact]
    public async Task CreateQuickMatch_ShouldFlowSeededPositionsIntoAnalysisLineup_ForAlternativeConfiguration()
    {
        // Arrange
        await ReapplySeedAsync();
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var matchId = Guid.NewGuid();

        // Act
        var match = await conn.QuerySingleAsync<QuickMatchRow>(
            @"SELECT tournamentid AS TournamentId, hometeamid AS HomeTeamId, guestteamid AS GuestTeamId
              FROM public.create_quick_match(@matchId, @sportId, @userId, @configId, FALSE)",
            new { matchId, sportId = WaterpoloSportId, userId = SeedUserId, configId = WaterpoloAltConfigId });

        // Assert
        match.TournamentId.Should().Be(AltTournamentId);
        match.HomeTeamId.Should().Be(HomeSquadId);
        match.GuestTeamId.Should().Be(OpponentSquadId);

        foreach (var teamId in new[] { match.HomeTeamId, match.GuestTeamId })
        {
            var rosterIds = (await conn.QueryAsync<Guid>(
                "SELECT id FROM public.playerrosters WHERE tournamentid = @tId AND teamid = @teamId",
                new { tId = match.TournamentId, teamId })).ToArray();

            var copied = await conn.ExecuteScalarAsync<int>(
                "SELECT public.copy_team_roster_to_match_lineup(@matchId, @teamId, @rosterIds)",
                new { matchId, teamId, rosterIds });
            copied.Should().Be(RosterLimit);

            var positions = (await conn.QueryAsync<string>(
                "SELECT positionshortname FROM public.get_match_analysis_lineup(@matchId, @teamId)",
                new { matchId, teamId })).ToList();

            positions.Should().HaveCount(RosterLimit);
            positions.Count(p => p == GoalkeeperShortName).Should().Be(2);
            positions.Count(p => p == UtilityShortName).Should().Be(RosterLimit - 2);
        }
    }

    private async Task<IEnumerable<SeededRosterRow>> GetRosterRowsAsync(Guid tournamentId, Guid teamId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        return await conn.QueryAsync<SeededRosterRow>(@"
            SELECT pr.tournamentid AS TournamentId, pr.teamid AS TeamId, pr.number AS Number, ppd.shortname AS PositionShortName
            FROM public.playerrosters pr
            JOIN public.playerpositiondefinitions ppd ON ppd.id = pr.positionid
            WHERE pr.tournamentid = @tournamentId AND pr.teamid = @teamId
            ORDER BY pr.number",
            new { tournamentId, teamId });
    }

    /// <summary>
    /// Resets the database (removing any seed already applied by the shared fixture) and re-applies the seed script.
    /// </summary>
    private async Task ReapplySeedAsync()
    {
        await Fixture.ResetDatabaseAsync();

        var sql = await File.ReadAllTextAsync(FindSeedScript());

        using var conn = Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql);
    }

    private static string FindSeedScript()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "TTA.DataAccess", "SQLScripts", "03-Infrastructure-and-Seed.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate 03-Infrastructure-and-Seed.sql walking up from the test output directory.");
    }
}
