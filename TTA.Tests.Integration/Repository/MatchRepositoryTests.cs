using Dapper;
using FluentAssertions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="MatchRepository"/> using a real database container.
/// Verifies SQL function calls, data mapping, and referential integrity constraints.
/// </summary>
public class MatchRepositoryTests : BaseIntegrationTest
{
    private readonly MatchRepository _repository;

    public MatchRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new MatchRepository(fixture.ConnectionFactory);
    }

    #region UpsertMatchAsync Tests

    /// <summary>
    /// Verifies that a new match is correctly persisted when all foreign keys (Tournament, Teams) exist.
    /// </summary>
    [Fact]
    public async Task UpsertMatchAsync_ShouldPersistNewMatch_WhenDataIsValid()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);

        // Act
        var result = await _repository.UpsertMatchAsync(match, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(match.Id);
        result.HomeTeamId.Should().Be(context.HomeTeamId);
    }

    /// <summary>
    /// Verifies that updating an existing match (e.g., recording a score) updates the database correctly.
    /// </summary>
    [Fact]
    public async Task UpsertMatchAsync_ShouldUpdateScores_WhenMatchExists()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);
        await _repository.UpsertMatchAsync(match, CancellationToken.None);

        // Act
        match.HomeScore = 3;
        match.GuestScore = 1;
        match.Temperature = 22.5;
        var result = await _repository.UpsertMatchAsync(match, CancellationToken.None);

        // Assert
        result.HomeScore.Should().Be(3);
        result.Temperature.Should().Be(22.5);

        var fromDb = await _repository.GetByIdAsync(match.Id, CancellationToken.None);
        fromDb!.HomeScore.Should().Be(3);
    }

    #endregion

    #region Retrieval Tests

    /// <summary>
    /// Verifies that GetMatchByIdWithDetailsAsync returns dynamic object with joined team names.
    /// </summary>
    [Fact]
    public async Task GetMatchByIdWithDetailsAsync_ShouldReturnJoinedData()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);
        await _repository.UpsertMatchAsync(match, CancellationToken.None);

        // Act
        var result = await _repository.GetMatchByIdWithDetailsAsync(match.Id, CancellationToken.None);

        // Assert
        // Dapper returns dynamic (DapperRow), we check properties defined in the SQL function
        ((Guid)result!.id).Should().Be(match.Id);
        ((string)result!.hometeamname).Should().NotBeNullOrEmpty();
        ((string)result!.tournamentname).Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Verifies retrieval of all matches associated with a specific tournament.
    /// </summary>
    [Fact]
    public async Task GetByTournamentIdAsync_ShouldReturnAllMatchesInTournament()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match1 = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);
        var match2 = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);

        await _repository.UpsertMatchAsync(match1, CancellationToken.None);
        await _repository.UpsertMatchAsync(match2, CancellationToken.None);

        // Act
        var results = await _repository.GetByTournamentIdAsync(context.TournamentId, CancellationToken.None);

        // Assert
        results.Should().HaveCount(2);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Seeds all necessary entities to satisfy foreign key constraints for a match.
    /// Matches require: User -> City -> Sport -> Config -> Tournament -> Teams -> Rosters[cite: 26, 27].
    /// </summary>
    private async Task<(Guid TournamentId, Guid HomeTeamId, Guid GuestTeamId)> SeedMatchEnvironmentAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // 1. Geography & Auth
        var userId = "auth0|test-match-owner";
        await conn.ExecuteAsync("INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @e, @n, NOW())",
            new { id = userId, e = "owner@match.com", n = "Owner" });

        var countryId = await conn.ExecuteScalarAsync<int>("INSERT INTO public.countries (name, code, createdat) VALUES ('MatchCountry', 'MTC', NOW()) RETURNING id");
        var regionId = await conn.ExecuteScalarAsync<int>("INSERT INTO public.regions (countryid, name) VALUES (@c, 'MatchRegion') RETURNING id", new { c = countryId });
        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.cities (id, regionid, name) VALUES (@id, @r, 'MatchCity')", new { id = cityId, r = regionId });

        // 2. Sport & Tournament
        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.sports (id, name) VALUES (@id, 'MatchSport')", new { id = sportId });
        var configId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) VALUES (@id, @s, false, 2, 45, 'Large', 20, 11)",
            new { id = configId, s = sportId });

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) VALUES (@id, @s, @cfg, @ct, @o, 'Test Tourney', NOW(), NOW())",
            new { id = tournamentId, s = sportId, cfg = configId, ct = cityId, o = userId });

        // 3. Teams & Mandatory Registration (The upsert_match function checks playerrosters[cite: 27])
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @c, 'MatchClub', NOW())", new { id = clubId, c = cityId });

        var homeTeamId = await SeedTeamAndRosterAsync(conn, clubId, sportId, tournamentId, "Home Team");
        var guestTeamId = await SeedTeamAndRosterAsync(conn, clubId, sportId, tournamentId, "Guest Team");

        return (tournamentId, homeTeamId, guestTeamId);
    }

    private async Task<Guid> SeedTeamAndRosterAsync(System.Data.IDbConnection conn, Guid clubId, Guid sportId, Guid tournamentId, string name)
    {
        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @c, @s, @n, 0, NOW())",
            new { id = teamId, c = clubId, s = sportId, n = name });

        // The stored function upsert_match requires teams to be in playerrosters[cite: 27]
        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @c, 'F', 'L', '2000-01-01', 0, NOW())",
            new { id = playerId, c = clubId });

        var posId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @s, 'Forward', 'FW')", new { id = posId, s = sportId });

        await conn.ExecuteAsync("SELECT public.upsert_player_to_roster(@id, @tid, @teamid, @pid, @posid, 10, NOW())",
            new { id = Guid.NewGuid(), tid = tournamentId, teamid = teamId, pid = playerId, posid = posId });

        return teamId;
    }

    private static Match CreateMatchModel(Guid tournamentId, Guid homeId, Guid guestId)
    {
        return new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            HomeTeamId = homeId,
            GuestTeamId = guestId,
            ScheduledAt = DateTime.UtcNow.AddHours(2),
            Venue = "Main Arena",
            CreatedAt = DateTime.UtcNow
        };
    }

    #endregion
}