using Dapper;
using FluentAssertions;
using System.Data.Common;
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

    private static readonly string[] ExpectedMatchNumbers = { "M-01", "M-02" };

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
    /// Verifies that GetByTournamentIdAsync correctly filters matches by the specified tournament ID.
    /// Seeds matches in multiple tournaments to ensure isolation and uses unique user IDs 
    /// to avoid primary key constraint violations.
    /// </summary>
    [Fact]
    public async Task GetByTournamentIdAsync_ShouldReturnOnlyMatchesInTargetTournament()
    {
        // Arrange
        // 1. Setup the target tournament context
        var context = await SeedMatchEnvironmentAsync();
        var match1 = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId, "M-01");
        var match2 = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId, "M-02");

        await _repository.UpsertMatchAsync(match1, CancellationToken.None);
        await _repository.UpsertMatchAsync(match2, CancellationToken.None);

        // 2. Setup a second tournament to verify that its matches are NOT returned
        var noiseContext = await SeedMatchEnvironmentAsync();
        var noiseMatch = CreateMatchModel(noiseContext.TournamentId, noiseContext.HomeTeamId, noiseContext.GuestTeamId, "NOISE-01");
        await _repository.UpsertMatchAsync(noiseMatch, CancellationToken.None);

        // Act
        var rawResults = await _repository.GetByTournamentIdAsync(context.TournamentId, CancellationToken.None);
        var resultsList = rawResults.ToList();

        // Assert
        resultsList.Should().HaveCount(2, "matches from other tournaments must be excluded from the result");

        // Verify each returned match belongs to the correct tournament
        foreach (var item in resultsList)
        {
            var dict = (IDictionary<string, object>)item;

            // Use TryGetValue to avoid double lookup (Sonar finding #1)
            if (!dict.TryGetValue("tournamentid", out var returnedIdObj) &&
                !dict.TryGetValue("TournamentId", out returnedIdObj))
            {
                throw new KeyNotFoundException("The expected TournamentId key was not found in the returned dynamic object.");
            }

            var returnedId = (Guid)returnedIdObj;
            returnedId.Should().Be(context.TournamentId);
        }

        // Verify that the specific match numbers are present
        var matchNumbers = resultsList.Select(x => (string)((IDictionary<string, object>)x)["matchnumber"]).ToList();

        // Fix: Use the static readonly field (Sonar finding #2)
        matchNumbers.Should().Contain(ExpectedMatchNumbers);
        matchNumbers.Should().NotContain("NOISE-01");
    }

    #endregion

    #region CreateQuickMatchAsync Tests

    /// <summary>
    /// Verifies that <see cref="MatchRepository.CreateQuickMatchAsync"/> provisions JIT teams, tournament container, 
    /// player rosters, and creates the match entity in a single atomic database operation.
    /// </summary>
    [Fact]
    public async Task CreateQuickMatchAsync_ShouldProvisionInfrastructureAndReturnProjection()
    {
        // Arrange - seed base sport and configuration with deferred FK transaction
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@sportId, 'Water Polo Quick', 'WPQ', @configId)",
            new { sportId, configId }, transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
            VALUES (@configId, @sportId, true, 4, 8, '30x20', 15, 13)",
            new { configId, sportId }, transaction: transaction);

        await transaction.CommitAsync();

        // Act
        var result = await _repository.CreateQuickMatchAsync(sportId, configId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();
        result.TournamentId.Should().NotBeEmpty();
        result.HomeTeamId.Should().NotBeEmpty();
        result.GuestTeamId.Should().NotBeEmpty();
        result.ScheduledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Seeds all necessary entities with unique names and valid codes strictly following 01-Tables.sql schema.
    /// Ensures country codes do not exceed the 3-character database limit (varchar(3)).
    /// Uses an explicit transaction to satisfy deferred foreign key constraints between sports and sportconfigurations.
    /// </summary>
    private async Task<(Guid TournamentId, Guid HomeTeamId, Guid GuestTeamId)> SeedMatchEnvironmentAsync()
    {
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        // Generate a unique 8-char suffix for names
        var suffix = Guid.NewGuid().ToString()[..8];
        // Generate a unique 3-char code for the country to satisfy varchar(3) constraint
        var shortCode = Guid.NewGuid().ToString()[..3].ToUpper();

        // 1. Geography & Auth
        var userId = $"auth0|{Guid.NewGuid()}";
        await conn.ExecuteAsync("INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @e, @n, NOW())",
            new { id = userId, e = $"owner_{suffix}@test.com", n = $"Owner {suffix}" }, transaction: transaction);

        var countryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.countries (name, code, createdat) VALUES (@n, @c, NOW()) RETURNING id",
            new { n = $"Country_{suffix}", c = shortCode }, transaction: transaction);

        var regionId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.regions (countryid, name) VALUES (@c, @n) RETURNING id",
            new { c = countryId, n = $"Region_{suffix}" }, transaction: transaction);

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.cities (id, regionid, name) VALUES (@id, @r, @n)",
            new { id = cityId, r = regionId, n = $"City_{suffix}" }, transaction: transaction);

        // 2. Sport & Tournament (Updated for Issue #69 schema)
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var shortName = $"S_{sportId:N}"[..10];

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @n, @sn, @cfg)",
            new { id = sportId, n = $"Sport_{suffix}", sn = shortName, cfg = configId }, transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @s, false, 2, 45, 'Large', 20, 11)",
            new { id = configId, s = sportId }, transaction: transaction);

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @s, @cfg, @ct, @o, @n, NOW(), NOW())",
            new { id = tournamentId, s = sportId, cfg = configId, ct = cityId, o = userId, n = $"Tourney_{suffix}" }, transaction: transaction);

        // 3. Teams & Rosters
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @c, @n, NOW())",
            new { id = clubId, c = cityId, n = $"Club_{suffix}" }, transaction: transaction);

        var homeTeamId = await SeedTeamAndRosterAsync(conn, transaction, clubId, sportId, tournamentId, $"Home_{suffix}");
        var guestTeamId = await SeedTeamAndRosterAsync(conn, transaction, clubId, sportId, tournamentId, $"Guest_{suffix}");

        await transaction.CommitAsync();

        return (tournamentId, homeTeamId, guestTeamId);
    }

    private static async Task<Guid> SeedTeamAndRosterAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction transaction,
        Guid clubId,
        Guid sportId,
        Guid tournamentId,
        string name)
    {
        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @c, @s, @n, 0, NOW())",
            new { id = teamId, c = clubId, s = sportId, n = name }, transaction: transaction);

        // The stored function upsert_match requires teams to be in playerrosters
        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @c, 'F', 'L', '2000-01-01', 0, NOW())",
            new { id = playerId, c = clubId }, transaction: transaction);

        var posId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @s, 'Forward', 'FW')",
            new { id = posId, s = sportId }, transaction: transaction);

        await conn.ExecuteAsync("SELECT public.upsert_player_to_roster(@id, @tid, @teamid, @pid, @posid, 10, NOW())",
            new { id = Guid.NewGuid(), tid = tournamentId, teamid = teamId, pid = playerId, posid = posId }, transaction: transaction);

        return teamId;
    }

    private static Match CreateMatchModel(Guid tournamentId, Guid homeId, Guid guestId, string matchNumber = "M-TEST")
    {
        return new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            HomeTeamId = homeId,
            GuestTeamId = guestId,
            MatchNumber = matchNumber,
            ScheduledAt = DateTime.UtcNow.AddHours(2),
            Venue = "Main Arena",
            CreatedAt = DateTime.UtcNow
        };
    }

    #endregion
}