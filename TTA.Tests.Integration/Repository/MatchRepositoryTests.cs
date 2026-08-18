using Dapper;
using FluentAssertions;
using System.Data.Common;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;
using TTA.Tests.Integration.Repository.Auth;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="MatchRepository"/> using a real database container.
/// Verifies SQL function calls, data mapping, and referential integrity constraints.
/// </summary>
public class MatchRepositoryTests : BaseIntegrationTest
{
    private readonly MatchRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchRepositoryTests"/> class with the database fixture.
    /// </summary>
    /// <param name="fixture">The database test container fixture.</param>
    public MatchRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new MatchRepository(fixture.ConnectionFactory);
    }

    private static readonly string[] ExpectedMatchNumbers = { "M-01", "M-02" };

    #region UpsertMatchAsync Tests

    /// <summary>
    /// Verifies that a new match is correctly persisted when all foreign keys (Tournament, Teams) exist.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
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
    /// <returns>A task representing the asynchronous test operation.</returns>
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
    /// <returns>A task representing the asynchronous test operation.</returns>
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
    /// <returns>A task representing the asynchronous test operation.</returns>
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

        matchNumbers.Should().Contain(ExpectedMatchNumbers);
        matchNumbers.Should().NotContain("NOISE-01");
    }

    #endregion

    #region CreateQuickMatchAsync Tests

    /// <summary>
    /// Verifies that <see cref="MatchRepository.CreateQuickMatchAsync"/> provisions JIT teams, tournament container, 
    /// player rosters for both Home and Guest teams, and creates the match entity in a single atomic database operation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateQuickMatchAsync_ShouldProvisionInfrastructureAndReturnMatchEntity()
    {
        // Arrange - seed base sport, configuration, JIT default club, users and players
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var defaultClubId = Guid.Parse("11111111-1111-1111-1111-000000000001");
        var tempCityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userId = $"auth0|quickmatch-{Guid.NewGuid()}";

        // 0. Ensure User exists for ownership assigning
        await conn.ExecuteAsync("INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @e, @n, NOW())",
            new { id = userId, e = $"quickmatch@test.com", n = $"QuickMatch Owner" }, transaction: transaction);

        // 1. Ensure JIT base geography exists
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) VALUES ('Ukraine', 'UA') ON CONFLICT DO NOTHING;
            INSERT INTO public.regions (countryid, name) SELECT id, 'Dnipro Region' FROM public.countries WHERE code = 'UA' ON CONFLICT DO NOTHING;
            INSERT INTO public.cities (id, regionid, name) SELECT @cityId, id, 'Dnipro' FROM public.regions WHERE name = 'Dnipro Region' ON CONFLICT DO NOTHING;",
            new { cityId = tempCityId }, transaction: transaction);

        // Resolve actual persisted city ID
        var actualCityId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM public.cities WHERE name = 'Dnipro' LIMIT 1",
            transaction: transaction);

        // 2. Ensure default club exists using actual city ID
        await conn.ExecuteAsync(@"
            INSERT INTO public.clubs (id, cityid, name, createdat) 
            VALUES (@clubId, @cityId, 'TTA Training Club', NOW()) 
            ON CONFLICT DO NOTHING;",
            new { clubId = defaultClubId, cityId = actualCityId }, transaction: transaction);

        // 3. Seed players for the default club so that create_quick_match function can register them into rosters
        for (int i = 1; i <= 6; i++)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
                VALUES (@id, @clubId, @fn, 'Test', '2000-01-01', 0, NOW())
                ON CONFLICT DO NOTHING;",
                new { id = Guid.NewGuid(), clubId = defaultClubId, fn = $"Player_{i}" }, transaction: transaction);
        }

        // 4. Seed sport and sport configuration (setting rosterlimit = 3 for clear testing)
        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@sportId, 'Water Polo Quick', 'WPQ', @configId)",
            new { sportId, configId }, transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
            VALUES (@configId, @sportId, true, 4, 8, '30x20', 3, 2)",
            new { configId, sportId }, transaction: transaction);

        await transaction.CommitAsync();

        // Act
        var result = await _repository.CreateQuickMatchAsync(sportId, userId, configId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();
        result.TournamentId.Should().NotBeEmpty();
        result.HomeTeamId.Should().NotBeEmpty();
        result.GuestTeamId.Should().NotBeEmpty();
        result.ScheduledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // Assert DB state: verify player rosters exist for BOTH Home and Guest teams up to rosterlimit (3 each)
        var homeRosterCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.playerrosters WHERE tournamentid = @tId AND teamid = @homeId",
            new { tId = result.TournamentId, homeId = result.HomeTeamId });

        var guestRosterCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.playerrosters WHERE tournamentid = @tId AND teamid = @guestId",
            new { tId = result.TournamentId, guestId = result.GuestTeamId });

        homeRosterCount.Should().Be(3, "home team roster should be filled up to rosterlimit (3)");
        guestRosterCount.Should().Be(3, "guest team roster should be filled up to rosterlimit (3)");
    }

    #endregion

    #region DeleteAsync Tests

    /// <summary>
    /// Verifies that <see cref="MatchRepository.DeleteAsync"/> deletes an existing match from the database and returns true.
    /// Also confirms that subsequent retrieval returns null.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteAsync_ShouldDeleteMatch_WhenMatchExists()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);
        await _repository.UpsertMatchAsync(match, CancellationToken.None);

        // Act
        var isDeleted = await _repository.DeleteAsync(match.Id, CancellationToken.None);

        // Assert
        isDeleted.Should().BeTrue("deleting an existing match should return true");

        var deletedMatch = await _repository.GetByIdAsync(match.Id, CancellationToken.None);
        deletedMatch.Should().BeNull("the match record must no longer exist in the database");
    }

    /// <summary>
    /// Verifies that <see cref="MatchRepository.DeleteAsync"/> returns false when trying to delete a non-existent match.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteAsync_ShouldReturnFalse_WhenMatchDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();

        // Act
        var isDeleted = await _repository.DeleteAsync(nonExistentMatchId, CancellationToken.None);

        // Assert
        isDeleted.Should().BeFalse("deleting a non-existent match should return false");
    }

    #endregion

    #region Report Repository Tests

    /// <summary>
    /// Verifies that <see cref="MatchRepository.GetTeamSummaryReportAsync"/> correctly aggregates 
    /// player statistics, action counts, and play percentage for a specific team in a match.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetTeamSummaryReportAsync_ShouldReturnTeamSummary_WhenDataExists()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);
        await _repository.UpsertMatchAsync(match, CancellationToken.None);

        // 1. Seed Time Anchors to establish match duration (Period 1: 0 to 10 minutes)
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var startTime = DateTime.UtcNow.AddMinutes(-30);
        var endTime = startTime.AddMinutes(10);

        await conn.ExecuteAsync(@"
            INSERT INTO public.timeanchors (id, matchid, periodnumber, type, timestamp) 
            VALUES 
            (@idStart, @matchId, 1, 0, @startTs),
            (@idEnd, @matchId, 1, 1, @endTs);",
            new
            {
                idStart = Guid.NewGuid(),
                idEnd = Guid.NewGuid(),
                matchId = match.Id,
                startTs = startTime,
                endTs = endTime
            });

        // 2. Fetch a lineup entry for the Home team
        var matchLineupId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT ml.id FROM public.matchlineups ml JOIN public.playerrosters pr ON ml.playerrosterid = pr.id WHERE ml.matchid = @matchId AND pr.teamid = @teamId LIMIT 1",
            new { matchId = match.Id, teamId = context.HomeTeamId });

        // If no lineup entry exists from seed, create one for testing
        Guid targetLineupId = matchLineupId;
        if (targetLineupId == Guid.Empty)
        {
            var rosterId = await conn.ExecuteScalarAsync<Guid>(
                "SELECT id FROM public.playerrosters WHERE tournamentid = @tId AND teamid = @teamId LIMIT 1",
                new { tId = context.TournamentId, teamId = context.HomeTeamId });

            targetLineupId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT INTO public.matchlineups (id, matchid, playerrosterid, number) VALUES (@id, @mId, @rId, 10)",
                new { id = targetLineupId, mId = match.Id, rId = rosterId });
        }

        // 3. Seed Player Presence (full 10 minutes duration -> 100% play percentage)
        await conn.ExecuteAsync(
            "INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout) VALUES (@id, @lineupId, 1, @in, @out)",
            new { id = Guid.NewGuid(), lineupId = targetLineupId, @in = startTime, @out = endTime });

        // 4. Seed an Event Definition and a Game Event ('Goal', positive, lead to goal)
        var eventDefId = Guid.NewGuid();
        var sportId = await conn.ExecuteScalarAsync<Guid>("SELECT sportid FROM public.tournaments WHERE id = @tId", new { tId = context.TournamentId });

        await conn.ExecuteAsync(
            "INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES (@id, @sId, 'Goal', 'G', true, NOW())",
            new { id = eventDefId, sId = sportId });

        await conn.ExecuteAsync(
            "INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat) VALUES (@id, @lineupId, @defId, 1, @ts, '00:05:00', true, NOW())",
            new { id = Guid.NewGuid(), lineupId = targetLineupId, defId = eventDefId, ts = startTime.AddMinutes(5) });

        // Act
        var report = (await _repository.GetTeamSummaryReportAsync(match.Id, context.HomeTeamId, CancellationToken.None)).ToList();

        // Assert
        report.Should().NotBeEmpty();
        var playerSummary = report.FirstOrDefault(r => r.MatchLineupId == targetLineupId);
        playerSummary.Should().NotBeNull();
        playerSummary!.Goals.Should().Be(1);
        playerSummary.TotalPositiveActions.Should().Be(1);
        playerSummary.PositiveGoalLeadingActions.Should().Be(1);
        playerSummary.PlayPercentage.Should().Be(100.0);
    }

    /// <summary>
    /// Verifies that <see cref="MatchRepository.GetPlayerDetailedReportAsync"/> retrieves the chronological 
    /// list of events for a specific player match lineup entry.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetPlayerDetailedReportAsync_ShouldReturnDetailedEvents_WhenDataExists()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId);
        await _repository.UpsertMatchAsync(match, CancellationToken.None);

        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // 1. Fetch or create a match lineup ID for the Home team
        var rosterId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT id FROM public.playerrosters WHERE tournamentid = @tId AND teamid = @teamId LIMIT 1",
            new { tId = context.TournamentId, teamId = context.HomeTeamId });

        var targetLineupId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.matchlineups (id, matchid, playerrosterid, number) VALUES (@id, @mId, @rId, 99)",
            new { id = targetLineupId, mId = match.Id, rId = rosterId });

        // 2. Seed Event Definitions
        var sportId = await conn.ExecuteScalarAsync<Guid>("SELECT sportid FROM public.tournaments WHERE id = @tId", new { tId = context.TournamentId });
        var eventDefId = Guid.NewGuid();

        await conn.ExecuteAsync(
            "INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES (@id, @sId, 'Exclusion', 'EX', false, NOW())",
            new { id = eventDefId, sId = sportId });

        // 3. Seed Game Events for this lineup
        var eventTimestamp = DateTime.UtcNow;
        await conn.ExecuteAsync(
            "INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat) VALUES (@id, @lineupId, @defId, 1, @ts, '00:03:30', false, NOW())",
            new { id = Guid.NewGuid(), lineupId = targetLineupId, defId = eventDefId, ts = eventTimestamp });

        // Act
        var report = (await _repository.GetPlayerDetailedReportAsync(match.Id, targetLineupId, CancellationToken.None)).ToList();

        // Assert
        report.Should().NotBeEmpty();
        report.Should().HaveCount(1);

        var detail = report[0];
        detail.MatchLineupId.Should().Be(targetLineupId);
        detail.Number.Should().Be(99);
        detail.EventName.Should().Be("Exclusion");
        detail.IsPositive.Should().BeFalse();
        detail.PeriodNumber.Should().Be(1);
        detail.NormalizedMatchTime.Should().Be(TimeSpan.FromMinutes(3.5));
    }

    /// <summary>
    /// Verifies that <see cref="MatchRepository.GetPlayerDetailedReportAsync"/> returns player events
    /// ordered strictly ascending by EventTimestamp, resolving any cross-period conflicts.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailedReportAsync_ShouldOrderEventsStrictlyByAscendingEventTimestamp()
    {
        // Arrange
        var context = await SeedMatchEnvironmentAsync();
        var match = CreateMatchModel(context.TournamentId, context.HomeTeamId, context.GuestTeamId, "M-REP-SORT");
        await _repository.UpsertMatchAsync(match);

        using var conn = Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Retrieve sportId associated with the seeded tournament
        var sportId = await conn.QueryFirstAsync<Guid>(
            "SELECT sportid FROM public.tournaments WHERE id = @tourId",
            new { tourId = context.TournamentId });

        // Query the existing player roster and position identifier using the exact column names from 01-Tables.sql
        var rosterData = await conn.QueryFirstAsync<dynamic>(
            "SELECT id, positionid FROM public.playerrosters WHERE teamid = @teamId AND tournamentid = @tourId LIMIT 1",
            new { teamId = context.HomeTeamId, tourId = context.TournamentId });

        Guid rosterId = rosterData.id;
        Guid positionId = rosterData.positionid;

        var lineupId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) VALUES (@id, @m, @r, 99, @posId)",
            new { id = lineupId, m = match.Id, r = rosterId, posId = positionId });

        // Seed event definition explicitly including shortname for the test sport
        var eventDefId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat)
            VALUES (@id, @sportId, 'Goal', 'GL', true, NOW())",
            new { id = eventDefId, sportId });

        var baseTime = DateTime.UtcNow;

        // Event 1: Period 2, absolute time is later (+30 min), but relative period time is smaller (2 min)
        await conn.ExecuteAsync(@"
            INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat)
            VALUES (@id, @mlId, @edId, 2, @ts, @norm, false, NOW())",
            new { id = Guid.NewGuid(), mlId = lineupId, edId = eventDefId, ts = baseTime.AddMinutes(30), norm = TimeSpan.FromMinutes(2) });

        // Event 2: Period 1, absolute time is earlier (baseTime), but relative period time is larger (44 min)
        await conn.ExecuteAsync(@"
            INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat)
            VALUES (@id, @mlId, @edId, 1, @ts, @norm, true, NOW())",
            new { id = Guid.NewGuid(), mlId = lineupId, edId = eventDefId, ts = baseTime, norm = TimeSpan.FromMinutes(44) });

        // Act
        var result = (await _repository.GetPlayerDetailedReportAsync(match.Id, lineupId)).ToList();

        // Assert
        result.Should().NotBeNull();
        var eventsWithData = result.Where(r => r.EventId.HasValue).ToList();
        eventsWithData.Should().HaveCount(2);

        // Verify that the repository (database function) orders strictly ascending by EventTimestamp
        eventsWithData[0].PeriodNumber.Should().Be(1);
        eventsWithData[0].EventTimestamp.Should().NotBeNull();
        eventsWithData[0].EventTimestamp!.Value.Should().BeCloseTo(baseTime, TimeSpan.FromMilliseconds(100));
        eventsWithData[0].IsLeadToGoal.Should().BeTrue();

        eventsWithData[1].PeriodNumber.Should().Be(2);
        eventsWithData[1].EventTimestamp.Should().NotBeNull();
        eventsWithData[1].EventTimestamp!.Value.Should().BeCloseTo(baseTime.AddMinutes(30), TimeSpan.FromMilliseconds(100));
        eventsWithData[1].IsLeadToGoal.Should().BeFalse();

        // Compare non-nullable DateTime values using .Value
        eventsWithData[0].EventTimestamp!.Value.Should().BeBefore(eventsWithData[1].EventTimestamp!.Value);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Seeds all necessary entities with unique names and valid codes strictly following 01-Tables.sql schema.
    /// Ensures country codes do not exceed the 3-character database limit (varchar(3)).
    /// Uses an explicit transaction to satisfy deferred foreign key constraints between sports and sportconfigurations.
    /// </summary>
    /// <returns>A tuple containing the created Tournament ID, Home Team ID, and Guest Team ID.</returns>
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

    /// <summary>
    /// Helper method to seed a team, a player, and their tournament roster entry within an active transaction.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    /// <param name="transaction">The active database transaction.</param>
    /// <param name="clubId">The club identifier to associate with the team and player.</param>
    /// <param name="sportId">The sport identifier associated with the team.</param>
    /// <param name="tournamentId">The tournament identifier for roster registration.</param>
    /// <param name="name">The name of the team to create.</param>
    /// <returns>The unique identifier of the newly created team.</returns>
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

    /// <summary>
    /// Helper method to construct a valid <see cref="Match"/> domain model for testing.
    /// </summary>
    /// <param name="tournamentId">The associated tournament identifier.</param>
    /// <param name="homeId">The home team identifier.</param>
    /// <param name="guestId">The guest team identifier.</param>
    /// <param name="matchNumber">The match number or code (defaults to "M-TEST").</param>
    /// <returns>A populated <see cref="Match"/> instance.</returns>
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