using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Rosters.DTOs;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the RostersController.
/// Validates roster management while ensuring proper authorization and data integrity.
/// </summary>
public class RostersControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    /// <summary>
    /// Gets the base URL for roster operations within a tournament.
    /// </summary>
    private static string GetBaseUrl(Guid tournamentId) => $"api/tournaments/{tournamentId}/rosters";

    /// <summary>
    /// Verifies that the system correctly retrieves the roster for a specific team.
    /// Route: GET api/tournaments/{tournamentId}/rosters/{teamId}
    /// </summary>
    [Fact]
    public async Task GetTeamRoster_ShouldReturnOk_WhenRosterExists()
    {
        // Arrange
        var userId = await SeedUserAsync(TestUserId);
        var tournamentId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        await SeedRequiredDataForRosterAsync(tournamentId, teamId, playerId, userId);

        // Act
        var response = await Client.GetAsync($"{GetBaseUrl(tournamentId)}/{teamId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<RosterPlayerResponse>>();
        result.Should().NotBeNull();
        result.Should().Contain(p => p.PlayerId == playerId);
    }

    /// <summary>
    /// Validates that a player can be added to a tournament team roster.
    /// Requires TeamEditor policy (Role <= 1).
    /// </summary>
    [Fact]
    public async Task AddPlayer_ShouldReturnCreated_WhenRequestIsValid()
    {
        // Arrange
        var userId = await SeedUserAsync(TestUserId);
        var tournamentId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var clubId = await SeedRequiredDataForRosterAsync(tournamentId, teamId, playerId, userId);

        var newPlayerId = Guid.NewGuid();
        await SeedPlayerAsync(newPlayerId, "Jane", "Smith", clubId);

        var sportId = await GetSportIdByTournament(tournamentId);
        var positionId = await SeedPositionAsync(sportId, "Midfielder", "MF");

        var request = new AddPlayerToRosterRequest(
            PlayerId: newPlayerId,
            PositionId: positionId,
            Number: 99
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{GetBaseUrl(tournamentId)}/{teamId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Ensures Conflict is returned when a jersey number is already taken in the team.
    /// Validates that authorization passes before business logic conflict is checked.
    /// </summary>
    [Fact]
    public async Task AddPlayer_ShouldReturnConflict_WhenJerseyNumberIsTaken()
    {
        // Arrange
        var userId = await SeedUserAsync(TestUserId);
        var tournamentId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var clubId = await SeedRequiredDataForRosterAsync(tournamentId, teamId, playerId, userId);

        var newPlayerId = Guid.NewGuid();
        await SeedPlayerAsync(newPlayerId, "Duplicate", "Number", clubId);

        var sportId = await GetSportIdByTournament(tournamentId);
        var positionId = await SeedPositionAsync(sportId, "Defender", "DF");

        var request = new AddPlayerToRosterRequest(
            PlayerId: newPlayerId,
            PositionId: positionId,
            Number: 10 // Pre-seeded number from SeedRequiredDataForRosterAsync
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{GetBaseUrl(tournamentId)}/{teamId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies player removal from a roster and ensures the state is updated in the database.
    /// </summary>
    [Fact]
    public async Task RemovePlayer_ShouldReturnNoContent_WhenPlayerExistsInRoster()
    {
        // Arrange
        var userId = await SeedUserAsync(TestUserId);
        var tournamentId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        // Seed all dependencies and the roster entry itself
        await SeedRequiredDataForRosterAsync(tournamentId, teamId, playerId, userId);

        var deleteUrl = $"{GetBaseUrl(tournamentId)}/{teamId}/{playerId}";

        // Act: Perform the deletion
        var deleteResponse = await Client.DeleteAsync(deleteUrl);

        // Assert: Immediate response should be 204 No Content
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Post-condition check: Verify the player is actually gone from the roster
        // We use the GET endpoint to fetch the current team roster
        var getRosterUrl = $"{GetBaseUrl(tournamentId)}/{teamId}";
        var getResponse = await Client.GetAsync(getRosterUrl);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var roster = await getResponse.Content.ReadFromJsonAsync<List<RosterPlayerResponse>>();

        // Assert that the list does not contain the deleted player
        roster.Should().NotBeNull();
        roster.Should().NotContain(p => p.PlayerId == playerId,
            "because the player should have been removed from the tournament roster");
    }

    #region Seeding Helpers

    /// <summary>
    /// Seeds the full dependency graph for a roster test case including authorization policies.
    /// </summary>
    private async Task<Guid> SeedRequiredDataForRosterAsync(Guid tournamentId, Guid teamId, Guid playerId, string userId)
    {
        var cityId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        await SeedRequiredLocationDataAsync(cityId);
        await SeedSportDataAsync(sportId, "Football");
        var configId = await SeedConfigurationAsync(sportId);

        await SeedClubAsync(clubId, cityId);
        await SeedTeamAsync(teamId, "Test Team", sportId, clubId);
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, userId, "Test Tournament");

        // IMPORTANT: Seed access policy so AccessService returns a valid role (Editor = 1)
        // TargetType 2 corresponds to TargetScope.Team
        await SeedAccessPolicyAsync(userId, teamId, 2, 1);

        var positionId = await SeedPositionAsync(sportId, "Forward", "FW");
        await SeedPlayerAsync(playerId, "First", "Last", clubId);
        await SeedPlayerRosterAsync(tournamentId, teamId, playerId, positionId, 10);

        return clubId;
    }

    /// <summary>
    /// Seeds an access policy in the auth schema to allow the test user to perform actions.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="targetId">The ID of the Team or Club.</param>
    /// <param name="targetType">0: Global, 1: Club, 2: Team.</param>
    /// <param name="role">0: FullControl, 1: Editor, 2: Viewer.</param>
    private async Task SeedAccessPolicyAsync(string userId, Guid targetId, int targetType, int role)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @userId, @role, @targetType, @targetId, NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("targetType", targetType);
        cmd.Parameters.AddWithValue("targetId", targetId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedTournamentAsync(Guid id, Guid sportId, Guid configId, Guid cityId, string userId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, enddate, createdat) 
            VALUES (@id, @sportId, @configId, @cityId, @userId, @name, @start, @end, NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sportId", sportId);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("cityId", cityId);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("start", DateTime.UtcNow.AddDays(-1));
        cmd.Parameters.AddWithValue("end", DateTime.UtcNow.AddDays(7));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedPlayerAsync(Guid id, string f, string l, Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
            VALUES (@id, @clubId, @f, @l, @dob, @gender, NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("clubId", clubId);
        cmd.Parameters.AddWithValue("f", f);
        cmd.Parameters.AddWithValue("l", l);
        cmd.Parameters.AddWithValue("dob", new DateTime(1995, 1, 1));
        cmd.Parameters.AddWithValue("gender", 0);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedPositionAsync(Guid sportId, string name, string shortName)
    {
        var id = Guid.NewGuid();
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sId, @name, @sn)";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sId", sportId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("sn", shortName);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedRequiredLocationDataAsync(Guid cityId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string countrySql = "INSERT INTO public.countries (name, code) VALUES ('Ukraine', 'UA') ON CONFLICT DO NOTHING RETURNING id";
        using var countryCmd = new NpgsqlCommand(countrySql, conn);
        var countryIdObj = await countryCmd.ExecuteScalarAsync();
        int countryId = countryIdObj != null ? (int)countryIdObj : 1;

        const string regionSql = "INSERT INTO public.regions (countryid, name) VALUES (@cid, 'Integration Region') ON CONFLICT DO NOTHING RETURNING id";
        using var regionCmd = new NpgsqlCommand(regionSql, conn);
        regionCmd.Parameters.AddWithValue("cid", countryId);
        var regionIdObj = await regionCmd.ExecuteScalarAsync();
        int regionId = regionIdObj != null ? (int)regionIdObj : 1;

        const string citySql = "INSERT INTO public.cities (id, regionid, name) VALUES (@id, @rid, 'Integration City') ON CONFLICT DO NOTHING";
        using var cityCmd = new NpgsqlCommand(citySql, conn);
        cityCmd.Parameters.AddWithValue("id", cityId);
        cityCmd.Parameters.AddWithValue("rid", regionId);
        await cityCmd.ExecuteNonQueryAsync();
    }

    private async Task SeedClubAsync(Guid id, Guid cityId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.clubs (id, name, cityid, createdat) VALUES (@id, 'Test Club', @cityId, NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("cityId", cityId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedTeamAsync(Guid id, string name, Guid sportId, Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @clubId, @sportId, @name, 0, NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("clubId", clubId);
        cmd.Parameters.AddWithValue("sportId", sportId);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedConfigurationAsync(Guid sportId)
    {
        var configId = Guid.NewGuid();
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) VALUES (@id, @sportId, false, 2, 45, 'Standard', 25, 11)";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", configId);
        cmd.Parameters.AddWithValue("sportId", sportId);
        await cmd.ExecuteNonQueryAsync();
        return configId;
    }

    private async Task SeedPlayerRosterAsync(Guid tId, Guid teamId, Guid pId, Guid posId, int number)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.playerrosters (id, tournamentid, teamid, playerid, positionid, number, createdat) VALUES (@id, @tId, @teamId, @pId, @posId, @num, NOW())";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tId", tId);
        cmd.Parameters.AddWithValue("teamId", teamId);
        cmd.Parameters.AddWithValue("pId", pId);
        cmd.Parameters.AddWithValue("posId", posId);
        cmd.Parameters.AddWithValue("num", number);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, 'Tester', NOW()) ON CONFLICT DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"test_{userId}@example.com");
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task SeedSportDataAsync(Guid id, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.sports (id, name) VALUES (@id, @name) ON CONFLICT DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> GetSportIdByTournament(Guid tournamentId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "SELECT sportid FROM public.tournaments WHERE id = @id";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", tournamentId);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }
    #endregion
}