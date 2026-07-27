using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.MatchLineupsController"/>.
/// Validates specific match lineup entry management, including retrieval, updates, and deletion.
/// </summary>
public class MatchLineupsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/matchlineups";

    #region Query Tests

    /// <summary>
    /// Verifies that any user can retrieve a specific lineup entry by its unique identifier.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnOk_WhenEntryExists()
    {
        // Arrange
        var context = await SetupMatchContextAsync(TestUserId);
        var entryId = await SeedMatchLineupEntryAsync(context.MatchId, context.RosterId, context.PositionId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{entryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MatchLineupResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(entryId);
    }

    /// <summary>
    /// Verifies that the system returns 404 Not Found when attempting to retrieve a non-existent lineup entry.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnNotFound_WhenEntryDoesNotExist()
    {
        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Command Tests

    /// <summary>
    /// Verifies that the tournament owner can update lineup entry details such as jersey number and position.
    /// </summary>
    [Fact]
    public async Task Update_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        var context = await SetupMatchContextAsync(TestUserId);
        var entryId = await SeedMatchLineupEntryAsync(context.MatchId, context.RosterId, context.PositionId);

        var request = new UpdatePlayerInMatchLineupRequest(
            Number: 99,
            PositionId: context.PositionId
        );

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{entryId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnedId = await response.Content.ReadFromJsonAsync<Guid>();
        returnedId.Should().Be(entryId);
    }

    /// <summary>
    /// Verifies that a user who does not own the tournament cannot update lineup entries.
    /// </summary>
    [Fact]
    public async Task Update_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var context = await SetupMatchContextAsync("auth0|someone-else");
        var entryId = await SeedMatchLineupEntryAsync(context.MatchId, context.RosterId, context.PositionId);

        var request = new UpdatePlayerInMatchLineupRequest(7, context.PositionId);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{entryId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that the tournament owner can successfully remove a player from the match lineup.
    /// </summary>
    [Fact]
    public async Task Delete_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        var context = await SetupMatchContextAsync(TestUserId);
        var entryId = await SeedMatchLineupEntryAsync(context.MatchId, context.RosterId, context.PositionId);

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{entryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var isDeleted = await response.Content.ReadFromJsonAsync<bool>();
        isDeleted.Should().BeTrue();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Sets up a full tournament context including match and team roster.
    /// </summary>
    private async Task<(Guid MatchId, Guid RosterId, Guid PositionId)> SetupMatchContextAsync(string ownerId)
    {
        await SeedUserAsync(ownerId);

        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);

        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Sport-" + Guid.NewGuid());
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, ownerId, "Tournament-" + Guid.NewGuid());

        var homeTeamId = await SeedTeamAsync(cityId, sportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(cityId, sportId, "Guest Team");

        var posId = await SeedPositionAsync(sportId, "Striker", "ST");
        var rosterId = await SeedTeamRegistrationAsync(tournamentId, homeTeamId, posId);
        await SeedTeamRegistrationAsync(tournamentId, guestTeamId, posId); // Ensure both teams registered

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, tournamentId, homeTeamId, guestTeamId, "ML-101");

        return (matchId, rosterId, posId);
    }

    private async Task<Guid> SeedMatchLineupEntryAsync(Guid matchId, Guid rosterId, Guid posId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        const string sql = @"
            INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid)
            VALUES (@id, @mId, @rId, 10, @pId)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("mId", matchId);
        cmd.Parameters.AddWithValue("rId", rosterId);
        cmd.Parameters.AddWithValue("pId", posId);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, @name, NOW()) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"{userId}@test.com");
        cmd.Parameters.AddWithValue("name", "Test User");
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedRequiredLocationDataAsync(Guid cityId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.countries (id, name, code) VALUES (1, 'TestCountry', 'TC') ON CONFLICT DO NOTHING", conn))
            await cmd.ExecuteNonQueryAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.regions (id, name, countryid) VALUES (1, 'TestRegion', 1) ON CONFLICT DO NOTHING", conn))
            await cmd.ExecuteNonQueryAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.cities (id, name, regionid) VALUES (@id, 'TestCity', 1) ON CONFLICT DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("id", cityId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand("INSERT INTO public.sports (id, name) VALUES (@id, @name) RETURNING id", conn);
        cmd.Parameters.AddWithValue("id", sportId);
        cmd.Parameters.AddWithValue("name", name);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid> SeedConfigurationAsync(Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        const string sql = "INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) VALUES (@id, @sid, false, 2, 45, 'Large', 22, 11)";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sid", sportId);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedTournamentAsync(Guid id, Guid sportId, Guid configId, Guid cityId, string ownerId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) VALUES (@id, @sId, @cId, @cityId, @ownerId, @name, NOW(), NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sId", sportId);
        cmd.Parameters.AddWithValue("cId", configId);
        cmd.Parameters.AddWithValue("cityId", cityId);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedTeamAsync(Guid cityId, Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        using (var cmd = new NpgsqlCommand("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cId, @n, NOW())", conn))
        {
            cmd.Parameters.AddWithValue("id", clubId);
            cmd.Parameters.AddWithValue("cId", cityId);
            cmd.Parameters.AddWithValue("n", "Club " + name);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = new NpgsqlCommand("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cId, @sId, @n, 0, NOW())", conn))
        {
            cmd.Parameters.AddWithValue("id", teamId);
            cmd.Parameters.AddWithValue("cId", clubId);
            cmd.Parameters.AddWithValue("sId", sportId);
            cmd.Parameters.AddWithValue("n", name);
            await cmd.ExecuteNonQueryAsync();
        }

        return teamId;
    }

    private async Task<Guid> SeedPositionAsync(Guid sportId, string name, string code)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        const string sql = "INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sId, @name, @code)";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sId", sportId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("code", code);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedTeamRegistrationAsync(Guid tournamentId, Guid teamId, Guid posId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var playerId = Guid.NewGuid();

        // Using a specific name to avoid scope conflicts with subsequent commands
        const string playerSql = "INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) SELECT @id, id, 'Player', 'Test', '1995-01-01', 0, NOW() FROM public.clubs LIMIT 1";
        using (var playerCmd = new NpgsqlCommand(playerSql, conn))
        {
            playerCmd.Parameters.AddWithValue("id", playerId);
            await playerCmd.ExecuteNonQueryAsync();
        }

        var rosterId = Guid.NewGuid();
        const string rosterSql = "INSERT INTO public.playerrosters (id, tournamentid, teamid, playerid, positionid, number, createdat) VALUES (@id, @tId, @teamId, @pId, @posId, 7, NOW())";

        // Using 'using var' with a unique name
        using var rosterCmd = new NpgsqlCommand(rosterSql, conn);
        rosterCmd.Parameters.AddWithValue("id", rosterId);
        rosterCmd.Parameters.AddWithValue("tId", tournamentId);
        rosterCmd.Parameters.AddWithValue("teamId", teamId);
        rosterCmd.Parameters.AddWithValue("pId", playerId);
        rosterCmd.Parameters.AddWithValue("posId", posId);

        await rosterCmd.ExecuteNonQueryAsync();

        return rosterId;
    }

    private async Task SeedMatchAsync(Guid id, Guid tournamentId, Guid homeId, Guid guestId, string number)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, matchnumber, createdat) VALUES (@id, @tId, @hId, @gId, NOW(), @num, NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tId", tournamentId);
        cmd.Parameters.AddWithValue("hId", homeId);
        cmd.Parameters.AddWithValue("gId", guestId);
        cmd.Parameters.AddWithValue("num", number);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}
