using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.MatchesController"/>.
/// Validates match retrieval and result recording, including authorization logic and database constraints.
/// </summary>
public class MatchesControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/matches";
    private readonly ITestOutputHelper _output = output; // Explicitly capture to avoid primary constructor warning

    #region Query Tests

    /// <summary>
    /// Verifies that any user can retrieve match details by its unique identifier.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnOk_WhenMatchExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-001");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MatchWithDetailsResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(matchId);
        result.HomeTeamName.Should().Be("Home FC");
        result.GuestTeamName.Should().Be("Guest FC");
    }

    #endregion

    #region Command Tests

    /// <summary>
    /// Verifies that the tournament owner can successfully record match results.
    /// Requires teams to be registered in the tournament roster to pass DB constraints.
    /// </summary>
    [Fact]
    public async Task RecordResult_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest");

        await SeedTeamRegistrationAsync(context.TournamentId, homeId, context.SportId);
        await SeedTeamRegistrationAsync(context.TournamentId, guestId, context.SportId);

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-SCORE-1");

        var request = new RecordMatchResultRequest(
            HomeScore: 3,
            GuestScore: 1,
            Temperature: 22.5
        );

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/result", request);

        // Assert
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync();
            // Using the explicit field _output to avoid the warning
            _output.WriteLine($"Error response: {error}");
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedId = await response.Content.ReadFromJsonAsync<Guid>();
        updatedId.Should().Be(matchId);
    }

    /// <summary>
    /// Verifies that a user cannot record results for a match in a tournament they do not own.
    /// </summary>
    [Fact]
    public async Task RecordResult_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var otherOwner = "auth0|stranger-danger";
        var context = await SetupTournamentContextAsync(otherOwner);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "H");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "G");

        await SeedTeamRegistrationAsync(context.TournamentId, homeId, context.SportId);
        await SeedTeamRegistrationAsync(context.TournamentId, guestId, context.SportId);

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-SECRET");

        var request = new RecordMatchResultRequest(2, 2, 15);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/result", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that invalid data (e.g., negative scores) results in a BadRequest response.
    /// </summary>
    [Fact]
    public async Task RecordResult_ShouldReturnBadRequest_WhenDataIsInvalid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var request = new RecordMatchResultRequest(-1, -1, 20);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/result", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Helpers

    private async Task<(Guid TournamentId, Guid CityId, Guid SportId)> SetupTournamentContextAsync(string ownerId)
    {
        await SeedUserAsync(ownerId);
        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "MatchSport-" + Guid.NewGuid());
        var configId = await SeedConfigurationAsync(sportId);
        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, ownerId, "MatchTourney-" + Guid.NewGuid());

        return (tournamentId, cityId, sportId);
    }

    private async Task SeedMatchAsync(Guid id, Guid tournamentId, Guid homeId, Guid guestId, string matchNumber)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, matchnumber, createdat) 
            VALUES (@id, @tId, @hId, @gId, @date, @num, @created)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tId", tournamentId);
        cmd.Parameters.AddWithValue("hId", homeId);
        cmd.Parameters.AddWithValue("gId", guestId);
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow.AddHours(2));
        cmd.Parameters.AddWithValue("num", matchNumber);
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedTeamRegistrationAsync(Guid tournamentId, Guid teamId, Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var posId = Guid.NewGuid();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sId, 'Dummy', 'DM') ON CONFLICT DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("id", posId);
            cmd.Parameters.AddWithValue("sId", sportId);
            await cmd.ExecuteNonQueryAsync();
        }

        var playerId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.clubs (id, cityid, name, createdat) SELECT @id, id, 'PlayerClub', NOW() FROM public.cities LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("id", clubId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = new NpgsqlCommand("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cId, 'John', 'Doe', '2000-01-01', 0, NOW())", conn))
        {
            cmd.Parameters.AddWithValue("id", playerId);
            cmd.Parameters.AddWithValue("cId", clubId);
            await cmd.ExecuteNonQueryAsync();
        }

        const string sql = @"
            INSERT INTO public.playerrosters (id, tournamentid, teamid, playerid, positionid, number, createdat)
            VALUES (@id, @tId, @teamId, @pId, @posId, @num, @created)";

        using var rosterCmd = new NpgsqlCommand(sql, conn);
        rosterCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        rosterCmd.Parameters.AddWithValue("tId", tournamentId);
        rosterCmd.Parameters.AddWithValue("teamId", teamId);
        rosterCmd.Parameters.AddWithValue("pId", playerId);
        rosterCmd.Parameters.AddWithValue("posId", posId);
        rosterCmd.Parameters.AddWithValue("num", new Random().Next(1, 99));
        rosterCmd.Parameters.AddWithValue("created", DateTime.UtcNow);

        await rosterCmd.ExecuteNonQueryAsync();
    }

    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.users (id, email, displayname, createdat)
            VALUES (@id, @email, @name, @created)
            ON CONFLICT (id) DO UPDATE SET displayname = EXCLUDED.displayname";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");
        cmd.Parameters.AddWithValue("name", $"User {userId}");
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedTournamentAsync(Guid id, Guid sportId, Guid configId, Guid cityId, string ownerId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat)
            VALUES (@id, @sportId, @configId, @cityId, @ownerId, @name, @start, @created)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sportId", sportId);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("cityId", cityId);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("start", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedTeamAsync(Guid cityId, Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        using (var cmd = new NpgsqlCommand("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cId, @n, @now)", conn))
        {
            cmd.Parameters.AddWithValue("id", clubId);
            cmd.Parameters.AddWithValue("cId", cityId);
            cmd.Parameters.AddWithValue("n", "Club " + name);
            cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = new NpgsqlCommand("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cId, @sId, @n, 0, @now)", conn))
        {
            cmd.Parameters.AddWithValue("id", teamId);
            cmd.Parameters.AddWithValue("cId", clubId);
            cmd.Parameters.AddWithValue("sId", sportId);
            cmd.Parameters.AddWithValue("n", name);
            cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }

        return teamId;
    }

    private async Task<Guid> SeedConfigurationAsync(Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        const string sql = "INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) VALUES (@id, @sid, false, 2, 45, 'Standard', 20, 11)";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sid", sportId);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedRequiredLocationDataAsync(Guid cityId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.countries (id, name, code) VALUES (1, 'Testland', 'TL') ON CONFLICT DO NOTHING", conn))
            await cmd.ExecuteNonQueryAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.regions (id, name, countryid) VALUES (1, 'TestRegion', 1) ON CONFLICT DO NOTHING", conn))
            await cmd.ExecuteNonQueryAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.cities (id, name, regionid) VALUES (@id, 'TestCity', 1) ON CONFLICT (id) DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("id", cityId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.sports (id, name) VALUES (@id, @name) ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name RETURNING id";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", sportId);
        cmd.Parameters.AddWithValue("name", name);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    #endregion
}