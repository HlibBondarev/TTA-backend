using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.DataAccess.Models;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.TournamentsController"/>.
/// Covers end-to-end scenarios including authentication, database persistence, and match-related operations.
/// </summary>
public class TournamentsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/tournaments";

    #region Tournament CRUD Tests

    /// <summary>
    /// Verifies that an anonymous user can retrieve a tournament by ID.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnOk_WhenTournamentExists()
    {
        // Arrange
        var userId = await SeedUserAsync(TestUserId);
        var cityId = await SeedRequiredLocationDataAsync(Guid.NewGuid());
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Football");
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, userId, "Championship");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{tournamentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<Tournament>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(tournamentId);
    }

    /// <summary>
    /// Verifies that a 404 status is returned when the tournament does not exist.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnNotFound_WhenTournamentDoesNotExist()
    {
        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that an authorized user can create a new tournament.
    /// </summary>
    [Fact]
    public async Task Create_ShouldReturnCreated_WhenDataIsValid()
    {
        // Arrange
        await SeedUserAsync(TestUserId);
        var cityId = await SeedRequiredLocationDataAsync(Guid.NewGuid());
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Basketball");
        var configId = await SeedConfigurationAsync(sportId);

        var request = new CreateTournamentRequest(
            SportId: sportId,
            ConfigurationId: configId,
            CityId: cityId,
            Name: "Summer Slam 2026",
            StartDate: DateTime.UtcNow.AddDays(10),
            EndDate: DateTime.UtcNow.AddDays(15)
        );

        // Act
        var response = await Client.PostAsJsonAsync(BaseUrl, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<TournamentResponse>();
        result.Should().NotBeNull();
        result!.Name.Should().Be(request.Name);
    }

    /// <summary>
    /// Verifies that an authorized owner can update their tournament.
    /// </summary>
    [Fact]
    public async Task Update_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        await SeedUserAsync(TestUserId);
        var cityId = await SeedRequiredLocationDataAsync(Guid.NewGuid());
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Tennis");
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, TestUserId, "Old Name");

        var updateRequest = new UpdateTournamentRequest(
            SportId: sportId,
            ConfigurationId: configId,
            CityId: cityId,
            Name: "Updated Tournament Name",
            StartDate: DateTime.UtcNow.AddDays(5),
            EndDate: DateTime.UtcNow.AddDays(7)
        );

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{tournamentId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TournamentResponse>();
        result!.Name.Should().Be("Updated Tournament Name");
    }

    /// <summary>
    /// Verifies that a user cannot update a tournament they do not own.
    /// </summary>
    [Fact]
    public async Task Update_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        await SeedUserAsync(TestUserId);
        var otherOwnerId = "auth0|other-user";
        await SeedUserAsync(otherOwnerId);

        var cityId = await SeedRequiredLocationDataAsync(Guid.NewGuid());
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Volleyball");
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, otherOwnerId, "Other's Tournament");

        var updateRequest = new UpdateTournamentRequest(sportId, configId, cityId, "Hacked Name", DateTime.UtcNow, null);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{tournamentId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Match Association Tests

    /// <summary>
    /// Verifies that a tournament owner can successfully schedule a match.
    /// </summary>
    [Fact]
    public async Task ScheduleMatch_ShouldReturnCreated_WhenUserIsOwnerAndDataIsValid()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");

        await SeedRosterAsync(context.TournamentId, homeTeamId);
        await SeedRosterAsync(context.TournamentId, guestTeamId);

        var request = new ScheduleMatchRequest(
            HomeTeamId: homeTeamId,
            GuestTeamId: guestTeamId,
            ScheduledAt: DateTime.UtcNow.AddDays(1),
            MatchNumber: "M-101",
            Venue: "Central Arena"
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{context.TournamentId}/matches", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var matchId = await response.Content.ReadFromJsonAsync<Guid>();
        matchId.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that scheduling fails if the user is not the owner of the tournament.
    /// Seeds valid teams and rosters first to ensure the request passes initial validation
    /// and reaches the ownership authorization check.
    /// </summary>
    [Fact]
    public async Task ScheduleMatch_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var otherOwner = "auth0|stranger-danger";
        // Create tournament owned by someone else
        var context = await SetupTournamentContextAsync(otherOwner);

        // Seed valid teams and rosters for THIS tournament
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Foreign Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Foreign Guest Team");
        await SeedRosterAsync(context.TournamentId, homeTeamId);
        await SeedRosterAsync(context.TournamentId, guestTeamId);

        // Request contains valid team IDs, so it passes DTO/existence validation
        var request = new ScheduleMatchRequest(
            HomeTeamId: homeTeamId,
            GuestTeamId: guestTeamId,
            ScheduledAt: DateTime.UtcNow.AddDays(1),
            MatchNumber: "FORBIDDEN-1",
            Venue: "Private Arena"
        );

        // Act
        // Current user (TestUserId) tries to modify otherOwner's tournament
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{context.TournamentId}/matches", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that any user can retrieve matches for a specific tournament.
    /// This test ensures the returned collection is not empty and validates the integrity 
    /// of the returned match data against the seeded values.
    /// </summary>
    [Fact]
    public async Task GetMatches_ShouldReturnOk_WhenTournamentExists()
    {
        // Arrange
        // Create a valid tournament context (Tournament, City, Sport)
        var context = await SetupTournamentContextAsync(TestUserId);

        // Seed teams and get their IDs
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");

        // Define specific data to seed and verify later
        var scheduledAt = DateTime.UtcNow.AddDays(1);
        var matchNumber = "M-FINAL-99";

        // Seed a match with controlled parameters
        await SeedMatchAsync(context.TournamentId, homeTeamId, guestTeamId, scheduledAt, matchNumber);

        // Act
        // Send request to retrieve all matches for the tournament
        var response = await Client.GetAsync($"{BaseUrl}/{context.TournamentId}/matches");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matches = await response.Content.ReadFromJsonAsync<IEnumerable<MatchWithDetailsResponse>>();

        // Comprehensive validation of the returned data
        matches.Should().NotBeNull();
        matches.Should().NotBeEmpty("The response should contain the seeded match.");
        matches.Should().HaveCount(1, "Only one match was seeded for this tournament.");

        var actualMatch = matches!.First();

        // Verify property-level integrity
        actualMatch.HomeTeamId.Should().Be(homeTeamId);
        actualMatch.GuestTeamId.Should().Be(guestTeamId);
        actualMatch.MatchNumber.Should().Be(matchNumber);

        // Verify date with 1-second precision to handle database storage differences
        actualMatch.ScheduledAt.Should().BeCloseTo(scheduledAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Verifies that scheduling a match fails with 409 Conflict when ScheduledAt is outside the tournament date range.
    /// The system throws a ConflictException because this is a business rule violation.
    /// </summary>
    [Fact]
    public async Task ScheduleMatch_ShouldReturnConflict_WhenDateIsOutsideTournamentRange()
    {
        // Arrange
        var ownerId = TestUserId;
        await SeedUserAsync(ownerId);

        var cityId = await SeedRequiredLocationDataAsync(Guid.NewGuid());

        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Sport-Range-Validation");
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();

        // Create a bounded tournament: from today+10 to today+20 days
        var startDate = DateTime.UtcNow.AddDays(10);
        var endDate = DateTime.UtcNow.AddDays(20);

        await SeedTournamentWithDatesAsync(tournamentId, sportId, configId, cityId, ownerId, "Bounded Tournament", startDate, endDate);

        var homeTeamId = await SeedTeamAsync(cityId, sportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(cityId, sportId, "Guest Team");
        await SeedRosterAsync(tournamentId, homeTeamId);
        await SeedRosterAsync(tournamentId, guestTeamId);

        // Attempt to schedule a match 5 days AFTER the tournament ends
        var request = new ScheduleMatchRequest(
            HomeTeamId: homeTeamId,
            GuestTeamId: guestTeamId,
            ScheduledAt: endDate.AddDays(5),
            MatchNumber: "M-OUT-OF-RANGE",
            Venue: "Boundary Stadium"
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{tournamentId}/matches", request);

        // Assert
        // Changed from BadRequest (400) to Conflict (409) to match business logic behavior
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Sets up a complete tournament context including user, location, sport, configuration, and tournament.
    /// </summary>
    /// <param name="ownerId">The owner user ID for the tournament.</param>
    /// <returns>A tuple containing the created TournamentId, CityId, and SportId.</returns>
    private async Task<(Guid TournamentId, Guid CityId, Guid SportId)> SetupTournamentContextAsync(string ownerId)
    {
        await SeedUserAsync(ownerId);
        var cityId = await SeedRequiredLocationDataAsync(Guid.NewGuid());
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Sport-" + Guid.NewGuid());
        var configId = await SeedConfigurationAsync(sportId);
        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, ownerId, "Tourney-" + Guid.NewGuid());

        return (tournamentId, cityId, sportId);
    }

    /// <summary>
    /// Seeds a user record into the database.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <returns>The seeded user ID.</returns>
    private async Task<string> SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.users (id, email, displayname, createdat)
            VALUES (@id, @email, @name, @created)
            ON CONFLICT (id) DO UPDATE SET displayname = EXCLUDED.displayname
            RETURNING id";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");
        cmd.Parameters.AddWithValue("name", $"Test User {userId}");
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);

        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Seeds a tournament record into the database.
    /// </summary>
    /// <param name="id">The unique identifier of the tournament.</param>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="configId">The unique identifier of the sport configuration.</param>
    /// <param name="cityId">The unique identifier of the city.</param>
    /// <param name="ownerId">The user ID of the tournament owner.</param>
    /// <param name="name">The name of the tournament.</param>
    private async Task SeedTournamentAsync(Guid id, Guid sportId, Guid configId, Guid cityId, string ownerId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, enddate, createdat)
            VALUES (@id, @sportId, @configId, @cityId, @ownerId, @name, @start, @end, @created)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sportId", sportId);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("cityId", cityId);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("start", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("end", DBNull.Value);
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds a club and team record into the database.
    /// </summary>
    /// <param name="cityId">The unique identifier of the city.</param>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="name">The team name.</param>
    /// <returns>The unique identifier of the seeded team.</returns>
    private async Task<Guid> SeedTeamAsync(Guid cityId, Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var teamId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        const string clubSql = "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cId, @name, @now)";
        using (var cmd = new NpgsqlCommand(clubSql, conn))
        {
            cmd.Parameters.AddWithValue("id", clubId);
            cmd.Parameters.AddWithValue("cId", cityId);
            cmd.Parameters.AddWithValue("name", "Club-" + name);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        const string teamSql = "INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cId, @sId, @name, 0, @now)";
        using (var cmd = new NpgsqlCommand(teamSql, conn))
        {
            cmd.Parameters.AddWithValue("id", teamId);
            cmd.Parameters.AddWithValue("cId", clubId);
            cmd.Parameters.AddWithValue("sId", sportId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        return teamId;
    }

    /// <summary>
    /// Seeds a player roster for a specific team in a tournament.
    /// Ensures player positions are reused if they already exist for the given sport.
    /// </summary>
    /// <param name="tournamentId">The unique identifier of the tournament.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    private async Task SeedRosterAsync(Guid tournamentId, Guid teamId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var playerId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // 1. Seed the player
        const string playerSql = @"
            INSERT INTO public.players (id, homeclubid, firstname, lastname, gender, birthdate, createdat) 
            VALUES (@id, (SELECT clubid FROM teams WHERE id=@tId), 'John', 'Doe', 0, @birth, @now)";

        using (var cmd = new NpgsqlCommand(playerSql, conn))
        {
            cmd.Parameters.AddWithValue("id", playerId);
            cmd.Parameters.AddWithValue("tId", teamId);
            cmd.Parameters.AddWithValue("birth", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Get or Create Player Position
        const string findPosSql = @"
            SELECT id FROM public.playerpositiondefinitions 
            WHERE sportid = (SELECT sportid FROM public.teams WHERE id = @tId) 
            AND name = 'Forward' LIMIT 1";

        Guid posId;
        using (var cmd = new NpgsqlCommand(findPosSql, conn))
        {
            cmd.Parameters.AddWithValue("tId", teamId);
            var result = await cmd.ExecuteScalarAsync();
            posId = result != null ? (Guid)result : Guid.Empty;
        }

        if (posId == Guid.Empty)
        {
            posId = Guid.NewGuid();
            const string insertPosSql = @"
                INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
                SELECT @id, sportid, 'Forward', 'FW' FROM public.teams WHERE id = @tId";

            using (var cmd = new NpgsqlCommand(insertPosSql, conn))
            {
                cmd.Parameters.AddWithValue("id", posId);
                cmd.Parameters.AddWithValue("tId", teamId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // 3. Seed the roster entry using the found-or-created posId
        const string rosterSql = @"
            INSERT INTO public.playerrosters (id, tournamentid, teamid, playerid, positionid, number, createdat) 
            VALUES (@id, @tourId, @teamId, @pId, @posId, 10, @now)";

        using (var cmd = new NpgsqlCommand(rosterSql, conn))
        {
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tourId", tournamentId);
            cmd.Parameters.AddWithValue("teamId", teamId);
            cmd.Parameters.AddWithValue("pId", playerId);
            cmd.Parameters.AddWithValue("posId", posId);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Seeds a match record into the database for integration testing.
    /// </summary>
    /// <param name="tournamentId">The ID of the tournament the match belongs to.</param>
    /// <param name="homeId">The ID of the home team.</param>
    /// <param name="guestId">The ID of the guest team.</param>
    /// <param name="scheduledAt">Optional: The scheduled date/time. Defaults to UtcNow.</param>
    /// <param name="matchNumber">Optional: The match identifier. Defaults to "M-TEST".</param>
    private async Task SeedMatchAsync(
        Guid tournamentId,
        Guid homeId,
        Guid guestId,
        DateTime? scheduledAt = null,
        string matchNumber = "M-TEST")
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, matchnumber, createdat) 
            VALUES (@id, @tId, @hId, @gId, @date, @num, @created)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tId", tournamentId);
        cmd.Parameters.AddWithValue("hId", homeId);
        cmd.Parameters.AddWithValue("gId", guestId);
        cmd.Parameters.AddWithValue("date", scheduledAt ?? DateTime.UtcNow);
        cmd.Parameters.AddWithValue("num", matchNumber);
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds or retrieves a sport configuration record for the specified sport.
    /// </summary>
    /// <param name="sportId">The unique identifier of the associated sport.</param>
    /// <returns>The unique identifier of the sport configuration.</returns>
    private async Task<Guid> SeedConfigurationAsync(Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        using (var checkCmd = new NpgsqlCommand("SELECT defaultconfigid FROM public.sports WHERE id = @sid", conn))
        {
            checkCmd.Parameters.AddWithValue("sid", sportId);
            var existingConfigId = await checkCmd.ExecuteScalarAsync();
            if (existingConfigId != null && existingConfigId != DBNull.Value)
            {
                return (Guid)existingConfigId;
            }
        }

        var configId = Guid.NewGuid();
        const string sql = @"
            INSERT INTO public.sportconfigurations (
                id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit
            ) VALUES (
                @id, @sid, false, 2, 45, 'Standard', 25, 11, 7)
            ON CONFLICT DO NOTHING";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", configId);
        cmd.Parameters.AddWithValue("sid", sportId);
        await cmd.ExecuteNonQueryAsync();

        return configId;
    }

    /// <summary>
    /// Seeds location data (Country, Region, City) required for tournament context.
    /// Uses name conflict resolution for countries and regions, and returns the persisted city identifier.
    /// </summary>
    /// <param name="cityId">The unique identifier of the city to seed.</param>
    /// <returns>The persisted unique identifier of the city.</returns>
    private async Task<Guid> SeedRequiredLocationDataAsync(Guid cityId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        await ExecuteSql(conn, @"
            INSERT INTO public.countries (name, code) 
            VALUES ('Ukraine', 'UA') 
            ON CONFLICT (name) DO NOTHING");

        int countryId;
        using (var cmd = new NpgsqlCommand("SELECT id FROM public.countries WHERE name = 'Ukraine'", conn))
        {
            countryId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        using (var cmd = new NpgsqlCommand(@"
            INSERT INTO public.regions (countryid, name) 
            VALUES (@countryId, 'Test Region') 
            ON CONFLICT (countryid, name) DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("countryId", countryId);
            await cmd.ExecuteNonQueryAsync();
        }

        int regionId;
        using (var cmd = new NpgsqlCommand("SELECT id FROM public.regions WHERE name = 'Test Region' AND countryid = @countryId", conn))
        {
            cmd.Parameters.AddWithValue("countryId", countryId);
            regionId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        const string citySql = @"
            INSERT INTO public.cities (id, name, regionid) 
            VALUES (@id, 'Test City', @regionId) 
            ON CONFLICT (regionid, name) DO UPDATE SET name = EXCLUDED.name 
            RETURNING id";
        using (var cmd = new NpgsqlCommand(citySql, conn))
        {
            cmd.Parameters.AddWithValue("id", cityId);
            cmd.Parameters.AddWithValue("regionId", regionId);
            return (Guid)(await cmd.ExecuteScalarAsync())!;
        }
    }

    /// <summary>
    /// Seeds a sport record along with its default sport configuration into the database.
    /// Uses a transaction to satisfy the deferred foreign key constraint between sports and sportconfigurations.
    /// </summary>
    /// <param name="sportId">The unique identifier of the sport to seed.</param>
    /// <param name="name">The display name of the sport.</param>
    /// <returns>The unique identifier of the created or updated sport.</returns>
    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        using (var checkCmd = new NpgsqlCommand("SELECT id FROM public.sports WHERE name = @name", conn))
        {
            checkCmd.Parameters.AddWithValue("name", name);
            var existing = await checkCmd.ExecuteScalarAsync();
            if (existing != null && existing != DBNull.Value)
            {
                return (Guid)existing;
            }
        }

        var configId = Guid.NewGuid();
        var shortName = name.Length <= 3 ? name.ToUpper() : name[..3].ToUpper();

        using var tx = await conn.BeginTransactionAsync();

        const string sportSql = @"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@sportId, @name, @shortName, @configId)";

        using (var cmd = new NpgsqlCommand(sportSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("sportId", sportId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("shortName", shortName);
            cmd.Parameters.AddWithValue("configId", configId);
            await cmd.ExecuteNonQueryAsync();
        }

        const string configSql = @"
            INSERT INTO public.sportconfigurations (
                id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit
            ) VALUES (
                @configId, @sportId, false, 2, 45, 'Standard', 25, 11, 7)";

        using (var cmd = new NpgsqlCommand(configSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("configId", configId);
            cmd.Parameters.AddWithValue("sportId", sportId);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        return sportId;
    }

    /// <summary>
    /// Seeds a tournament with specific start and end dates to test boundary conditions.
    /// </summary>
    /// <param name="id">The unique identifier of the tournament.</param>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="configId">The unique identifier of the sport configuration.</param>
    /// <param name="cityId">The unique identifier of the city.</param>
    /// <param name="ownerId">The user ID of the tournament owner.</param>
    /// <param name="name">The name of the tournament.</param>
    /// <param name="start">The start date of the tournament.</param>
    /// <param name="end">The end date of the tournament.</param>
    private async Task SeedTournamentWithDatesAsync(Guid id, Guid sportId, Guid configId, Guid cityId, string ownerId, string name, DateTime start, DateTime? end)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, enddate, createdat)
            VALUES (@id, @sportId, @configId, @cityId, @ownerId, @name, @start, @end, @created)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sportId", sportId);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("cityId", cityId);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Executes a non-query SQL command against the active database connection.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    /// <param name="sql">The SQL statement to execute.</param>
    private static async Task ExecuteSql(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}