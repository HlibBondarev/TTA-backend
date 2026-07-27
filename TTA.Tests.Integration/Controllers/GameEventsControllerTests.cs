using Dapper;
using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the GameEventsController.
/// Ensures correct behavior for retrieving and deleting game events with full database dependency tracking.
/// </summary>
public class GameEventsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/gameevents";

    #region GET Tests

    /// <summary>
    /// Verifies that an existing game event can be retrieved and correctly mapped to a <see cref="GameEventResponse"/>.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnOk_WhenEventExists()
    {
        // Arrange
        var context = await SetupGameEventContextAsync(TestUserId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{context.EventId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<GameEventResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(context.EventId);
        result.EventName.Should().Be("Goal");
        result.PlayerNumber.Should().Be(10);
    }

    /// <summary>
    /// Verifies that searching for a non-existent GUID returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnNotFound_WhenEventDoesNotExist()
    {
        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region DELETE Tests

    /// <summary>
    /// Verifies that a tournament owner can delete a game event associated with their tournament.
    /// </summary>
    [Fact]
    public async Task Delete_ShouldReturnNoContent_WhenUserIsTournamentOwner()
    {
        // Arrange
        var context = await SetupGameEventContextAsync(TestUserId);

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{context.EventId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var exists = await CheckIfEventExists(context.EventId);
        exists.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a user who does not own the tournament is forbidden from deleting the event.
    /// </summary>
    [Fact]
    public async Task Delete_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var context = await SetupGameEventContextAsync("auth0|different-owner-id");

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{context.EventId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that deleting a non-existent event returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task Delete_ShouldReturnNotFound_WhenEventDoesNotExist()
    {
        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Directly queries the database to verify the deletion of a record.
    /// </summary>
    private async Task<bool> CheckIfEventExists(Guid eventId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM public.gameevents WHERE id = @id",
            new { id = eventId });
        return count > 0;
    }

    /// <summary>
    /// Seeds the necessary database records to create a valid GameEvent context.
    /// Manually populates the hierarchy to satisfy foreign key constraints.
    /// </summary>
    /// <param name="ownerId">The unique identifier of the tournament owner.</param>
    /// <returns>A tuple containing the IDs of the created entities.</returns>
    private async Task<(Guid TournamentId, Guid MatchId, Guid EventId)> SetupGameEventContextAsync(string ownerId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // 1. Geography
        var countryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.countries (name, code, createdat) VALUES (@name, @code, NOW()) RETURNING id",
            new { name = $"Country_{Guid.NewGuid()}", code = Guid.NewGuid().ToString()[..3].ToUpper() });

        var regionId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.regions (countryid, name) VALUES (@cId, @name) RETURNING id",
            new { cId = countryId, name = "Integration Region" });

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.cities (id, regionid, name) VALUES (@id, @rId, 'Integration City')",
            new { id = cityId, rId = regionId });

        // 2. User
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) 
            VALUES (@id, @email, @name, NOW()) 
            ON CONFLICT (id) DO NOTHING",
            new { id = ownerId, email = $"{Guid.NewGuid()}@tta-test.com", name = "Test Developer" });

        // 3. Sport & Configuration
        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.sports (id, name) VALUES (@id, @name)",
            new { id = sportId, name = $"Sport_{Guid.NewGuid()}" });

        var configId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @sId, true, 2, 45, 'Standard', 11, 11)",
            new { id = configId, sId = sportId });

        // 4. Tournament
        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sId, @cId, @cityId, @owner, 'Integration Tournament', NOW(), NOW())",
            new { id = tournamentId, sId = sportId, cId = configId, cityId, owner = ownerId });

        // 5. Club & Teams
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityId, 'Test Club', NOW())",
            new { id = clubId, cityId });

        var homeId = Guid.NewGuid();
        var guestId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @clubId, @sId, 'Home', 0, NOW())",
            new { id = homeId, clubId, sId = sportId });
        await conn.ExecuteAsync(@"INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @clubId, @sId, 'Guest', 0, NOW())",
            new { id = guestId, clubId, sId = sportId });

        // 6. Match
        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, matchnumber, createdat) 
            VALUES (@id, @tId, @hId, @gId, NOW(), 'M-001', NOW())",
            new { id = matchId, tId = tournamentId, hId = homeId, gId = guestId });

        // 7. Player, Position & Roster
        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
            VALUES (@id, @clubId, 'Integration', 'Player', '1990-01-01', 0, NOW())", new { id = playerId, clubId });

        var posId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sId, 'Forward', 'FW')",
            new { id = posId, sId = sportId });

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) 
            VALUES (@id, @pId, @tId, @teamId, 10, @posId, NOW())",
            new { id = rosterId, pId = playerId, tId = tournamentId, teamId = homeId, posId });

        // 8. Lineup
        var lineupId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) 
            VALUES (@id, @mId, @rId, 10, @posId)",
            new { id = lineupId, mId = matchId, rId = rosterId, posId });

        // 9. Event Definition & Game Event
        var defId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat) 
            VALUES (@id, @sId, 'Goal', 'G', true, NOW())", new { id = defId, sId = sportId });

        var eventId = Guid.NewGuid();
        // Ensure the timestamp is slightly in the past to avoid future-date validation issues
        await conn.ExecuteAsync(@"
            INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, isleadtogoal, createdat) 
            VALUES (@id, @lId, @dId, 1, @ts, false, NOW())",
            new { id = eventId, lId = lineupId, dId = defId, ts = DateTime.UtcNow.AddMinutes(-5) });

        return (tournamentId, matchId, eventId);
    }

    #endregion
}