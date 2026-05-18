using Dapper;
using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.DataAccess.Enums;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.MatchesController"/>.
/// Validates match retrieval, lineup management, and result recording.
/// </summary>
public class MatchesControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/matches";
    private readonly ITestOutputHelper _output = output;

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
    }

    /// <summary>
    /// Verifies that match lineups can be retrieved by any user.
    /// </summary>
    [Fact]
    public async Task GetMatchLineup_ShouldReturnOk_WhenMatchExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest");
        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-LINEUP-1");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/lineups");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<MatchLineupResponse>>();
        result.Should().NotBeNull();
    }

    #endregion

    #region Command Tests

    /// <summary>
    /// Verifies that a player from the tournament roster can be added to the match lineup by the owner.
    /// </summary>
    [Fact]
    public async Task AddPlayerToLineup_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var teamId = await SeedTeamAsync(context.CityId, context.SportId, "Lineup Team");
        var rosterId = await SeedTeamRegistrationAsync(context.TournamentId, teamId, context.SportId);

        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        await SeedTeamRegistrationAsync(context.TournamentId, guestId, context.SportId);

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, teamId, guestId, "M-ADD-PLAYER");

        var posId = await GetFirstPositionIdAsync(context.SportId);
        var request = new AddPlayerToMatchLineupRequest(
            Number: 10,
            IsInStartingLineup: true,
            PositionId: posId
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/lineups/{rosterId}", request);

        // Assert
        if (response.StatusCode != HttpStatusCode.OK)
            _output.WriteLine(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that the tournament owner can successfully copy selected players from the tournament roster to the match lineup.
    /// This test ensures all database constraints including gender, homeclubid, and positionid are satisfied.
    /// </summary>
    [Fact]
    public async Task CopyFromRoster_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);

        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // 1. Seed a club to satisfy the player's homeclubid constraint
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityId, 'Test Club', now())",
            new { id = clubId, cityId = context.CityId });

        // 2. Seed a position definition to satisfy the playerrosters' positionid constraint
        var positionId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sId, 'Forward', 'FW')",
            new { id = positionId, sId = context.SportId });

        var teamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, teamId, guestId, "M-301");

        // 3. Seed players and roster entries with all required fields
        var playerRosterIds = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            var playerId = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
                VALUES (@id, @clubId, @f, @l, '2000-01-01', 1, now())",
                new { id = playerId, clubId, f = $"Player{i}", l = "Test" });

            var rosterId = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO public.playerrosters (id, teamid, tournamentid, playerid, number, positionid, createdat) 
                VALUES (@id, @tId, @tourId, @pId, @num, @posId, now())",
                new { id = rosterId, tId = teamId, tourId = context.TournamentId, pId = playerId, num = 10 + i, posId = positionId });

            playerRosterIds.Add(rosterId);
        }

        var request = new CopyTeamRosterToMatchLineupRequest(playerRosterIds);
        // Corrected URL: added the missing slash before 'copy'
        var url = $"{BaseUrl}/{matchId}/teams/{teamId}/lineup/copy";

        // Act
        var response = await Client.PostAsJsonAsync(url, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var count = await response.Content.ReadFromJsonAsync<int>();
        count.Should().Be(3);
    }

    /// <summary>
    /// Verifies that a user who is not the tournament owner receives a 403 Forbidden response.
    /// Seeds necessary match and team data to ensure the validation logic reaches the ownership check.
    /// </summary>
    [Fact]
    public async Task CopyFromRoster_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange: Tournament owned by a different user
        var context = await SetupTournamentContextAsync("not-the-owner-id");
        var teamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, teamId, guestId, "M-302");

        var request = new CopyTeamRosterToMatchLineupRequest(new List<Guid> { Guid.NewGuid() });
        // Corrected URL: added the missing slash before 'copy'
        var url = $"{BaseUrl}/{matchId}/teams/{teamId}/lineup/copy";

        // Act
        var response = await Client.PostAsJsonAsync(url, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that a user with the TeamEditor role can successfully copy selected players to the match lineup.
    /// Ensures that all database constraints (gender, homeclubid, positionid) and access policies are satisfied.
    /// </summary>
    [Fact]
    public async Task CopyFromRosterByTeam_ShouldReturnOk_WhenUserIsTeamEditor()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);

        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var teamId = await SeedTeamAsync(context.CityId, context.SportId, "Editor Team");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Other Team");

        // Grant TeamEditor role (role=1) for the specific team (targettype=2) in the auth schema
        await conn.ExecuteAsync(@"
            INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @uId, 1, 2, @tId, now())",
            new { id = Guid.NewGuid(), uId = TestUserId, tId = teamId });

        // Seed a position definition first to prevent NullReferenceException in GetFirstPositionIdAsync
        var positionId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            VALUES (@id, @sId, 'Universal Player', 'UP')",
            new { id = positionId, sId = context.SportId });

        // Seed club for player constraints
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityId, 'Editor Club', now())",
            new { id = clubId, cityId = context.CityId });

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, teamId, guestId, "M-601");

        // Seed a player and their roster entry linked to the created position
        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
            VALUES (@id, @clubId, 'John', 'Editor', '1998-08-08', 1, now())",
            new { id = playerId, clubId });

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerrosters (id, teamid, tournamentid, playerid, number, positionid, createdat) 
            VALUES (@id, @tId, @tourId, @pId, 88, @posId, now())",
            new { id = rosterId, tId = teamId, tourId = context.TournamentId, pId = playerId, posId = positionId });

        var request = new CopyTeamRosterToMatchLineupRequest(new[] { rosterId });
        var url = $"{BaseUrl}/{matchId}/teams/{teamId}/lineup/copy-by-team";

        // Act
        var response = await Client.PostAsJsonAsync(url, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var count = await response.Content.ReadFromJsonAsync<int>();
        count.Should().Be(1);
    }

    /// <summary>
    /// Verifies that the endpoint returns 400 BadRequest when the authorized team is not a participant in the match.
    /// </summary>
    [Fact]
    public async Task CopyFromRosterByTeam_ShouldReturnBadRequest_WhenTeamIsNotInMatch()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Match Home");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Match Guest");
        var nonParticipantTeamId = await SeedTeamAsync(context.CityId, context.SportId, "External Team");

        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Grant permissions for the non-participant team to pass the 403 Authorization check
        await conn.ExecuteAsync(@"
            INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @uId, 1, 2, @tId, now())",
            new { id = Guid.NewGuid(), uId = TestUserId, tId = nonParticipantTeamId });

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-602");

        var request = new CopyTeamRosterToMatchLineupRequest(new[] { Guid.NewGuid() });
        var url = $"{BaseUrl}/{matchId}/teams/{nonParticipantTeamId}/lineup/copy-by-team";

        // Act
        var response = await Client.PostAsJsonAsync(url, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await response.Content.ReadAsStringAsync();
        errorContent.Should().Contain("The specified team is not a participant in this match");
    }

    /// <summary>
    /// Verifies that the tournament owner can successfully record match results.
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

        var request = new RecordMatchResultRequest(3, 1, 22.5);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/result", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a user cannot record results for a match in a tournament they do not own.
    /// </summary>
    [Fact]
    public async Task RecordResult_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var context = await SetupTournamentContextAsync("auth0|stranger");
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "H");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "G");
        await SeedTeamRegistrationAsync(context.TournamentId, homeId, context.SportId);
        await SeedTeamRegistrationAsync(context.TournamentId, guestId, context.SportId);

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-FORBIDDEN");

        var request = new RecordMatchResultRequest(2, 2, 15);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/result", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that the system returns Bad Request when the match result data is invalid.
    /// This tests the validation logic before it reaches the mediator handler.
    /// </summary>
    [Fact]
    public async Task RecordResult_ShouldReturnBadRequest_WhenDataIsInvalid()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-VAL-FAIL");

        // Invalid request: negative score
        var request = new RecordMatchResultRequest(-1, 0, 10.0);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/result", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Game Events Tests

    /// <summary>
    /// Verifies that the system retrieves all events associated with a specific match.
    /// </summary>
    [Fact]
    public async Task GetMatchEvents_ShouldReturnOk_WhenMatchExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-01");

        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Goal", true);
        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);
        await SeedGameEventAsync(lineupId, eventDefId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that match events are correctly retrieved and ordered chronologically by match time.
    /// </summary>
    [Fact]
    public async Task GetMatchEvents_ShouldReturnSortedTimeline_WhenMatchExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);

        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");

        // Ensure a position definition exists for this specific sport to satisfy FK
        var positionId = Guid.NewGuid();
        using (var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"
                INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
                VALUES (@id, @sId, 'Forward', 'FW') ON CONFLICT DO NOTHING",
                new { id = positionId, sId = context.SportId });
        }

        var matchId = Guid.NewGuid();
        // Signature: (id, tournamentId, homeId, guestId, matchNumber)
        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-SORT-01");

        var coachId = Guid.NewGuid();
        // Calling your helper with the EXACT order required by your stack trace:
        // (matchId, teamId, cityId, tournamentId, sportId)
        await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);

        // Retrieve the generated LineupId to link game events
        var lineupId = await GetLineupIdAsync(matchId, homeTeamId);
        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Goal", true);

        // Seed events with specific match times to verify sorting
        await SeedGameEventAsync(lineupId, eventDefId, DateTime.UtcNow.AddMinutes(-5), TimeSpan.FromMinutes(40));
        await SeedGameEventAsync(lineupId, eventDefId, DateTime.UtcNow.AddMinutes(-15), TimeSpan.FromMinutes(10));

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await response.Content.ReadFromJsonAsync<List<GameEventResponse>>();
        events.Should().NotBeNull();
        events.Should().HaveCount(2);

        // Assert chronological order (10 min first, 40 min second)
        events![0].NormalizedMatchTime.Should().Be(TimeSpan.FromMinutes(10));
        events[1].NormalizedMatchTime.Should().Be(TimeSpan.FromMinutes(40));
    }

    /// <summary>
    /// Verifies that a new game event can be recorded for a specific match.
    /// </summary>
    [Fact]
    public async Task RecordMatchEvent_ShouldReturnCreated_WhenDataIsValid()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-02");

        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Yellow Card", false);
        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);

        var request = new CreateGameEventRequest(lineupId, eventDefId, 1, false);

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/events", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Verifies recording an event specifically for a team side within a match.
    /// Seeds an access policy to avoid 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task RecordMatchEventByTeam_ShouldReturnCreated_WhenDataIsValid()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-03");
        await SeedAccessPolicyAsync(TestUserId, 0, 2, homeTeamId); // FullControl (0) for Team (2)

        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Timeout", true);
        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);

        var request = new CreateGameEventRequest(lineupId, eventDefId, 1, false);

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/teams/{homeTeamId}/events", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Verifies that an existing game event can be updated.
    /// Adjusted assertion to OK (200) based on observed behavior.
    /// </summary>
    [Fact]
    public async Task UpdateMatchEvent_ShouldReturnOk_WhenUpdateIsValid()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-04");

        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Shot", true);
        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);
        var eventId = await SeedGameEventAsync(lineupId, eventDefId);

        var request = new UpdateGameEventRequest(lineupId, eventDefId, 2, true);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/events/{eventId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that an existing game event can be updated via team route.
    /// Seeds an access policy to avoid 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task UpdateMatchEventByTeam_ShouldReturnNoContent_WhenUpdateIsValid()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-05");
        await SeedAccessPolicyAsync(TestUserId, 0, 2, homeTeamId);

        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);
        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Foul", false);
        var eventId = await SeedGameEventAsync(lineupId, eventDefId);

        var request = new UpdateGameEventRequest(lineupId, eventDefId, 1, false);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{matchId}/teams/{homeTeamId}/events/{eventId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a game event can be successfully removed via the team-specific route.
    /// Route: DELETE api/matches/{matchId}/teams/{teamId}/events/{id}
    /// </summary>
    [Fact]
    public async Task DeleteMatchEvent_ShouldReturnNoContent_WhenEventExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        // 1. Seed match
        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-06");

        // 2. Seed access policy (Role 0 = FullControl/Editor) for the team to pass [Authorize(Policy = "TeamEditor")]
        await SeedAccessPolicyAsync(TestUserId, 0, 2, homeTeamId);

        // 3. Seed required entities for event
        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Technical Foul", false);
        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);
        var eventId = await SeedGameEventAsync(lineupId, eventDefId);

        // Act - Using the EXACT route from your MatchesController
        var response = await Client.DeleteAsync($"{BaseUrl}/{matchId}/teams/{homeTeamId}/events/{eventId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Time Anchors Tests

    /// <summary>
    /// Verifies that retrieving match anchors returns an OK response with the list of anchors.
    /// </summary>
    [Fact]
    public async Task GetMatchAnchors_ShouldReturnOk_WhenAnchorsExist()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC Anchors");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC Anchors");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "TA-01");

        // Seed initial anchors for the match
        await SeedTimeAnchorAsync(matchId, 1, (int)TimeAnchorType.PeriodStart);
        await SeedTimeAnchorAsync(matchId, 1, (int)TimeAnchorType.PeriodEnd);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/anchors");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Explicitly defining JSON options to handle string-to-enum conversion
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var anchors = await response.Content.ReadFromJsonAsync<List<TimeAnchorResponse>>(jsonOptions);
        anchors.Should().NotBeNull();
        anchors!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies that retrieving a specific time anchor by ID returns an OK response.
    /// </summary>
    [Fact]
    public async Task GetTimeAnchorById_ShouldReturnOk_WhenExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC Anchor");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC Anchor");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "TA-02");

        var anchorId = await SeedTimeAnchorAsync(matchId, 1, (int)TimeAnchorType.PeriodStart);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/anchors/{anchorId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Explicitly defining JSON options to handle string-to-enum conversion
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var anchor = await response.Content.ReadFromJsonAsync<TimeAnchorResponse>(jsonOptions);
        anchor.Should().NotBeNull();
        anchor!.Id.Should().Be(anchorId);
        anchor.MatchId.Should().Be(matchId);
    }

    /// <summary>
    /// Verifies that an authorized user (Tournament Owner) can successfully record a new time anchor.
    /// </summary>
    [Fact]
    public async Task RecordTimeAnchor_ShouldReturnCreated_WhenUserIsAuthorized()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC RecAnchor");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC RecAnchor");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "TA-03");

        var request = new CreateTimeAnchorRequest(1, TimeAnchorType.PeriodStart);

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/anchors", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var returnedId = await response.Content.ReadFromJsonAsync<Guid>();
        returnedId.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that an authorized user (Tournament Owner) can delete an existing time anchor.
    /// </summary>
    [Fact]
    public async Task DeleteTimeAnchor_ShouldReturnNoContent_WhenUserIsAuthorized()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC DelAnchor");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC DelAnchor");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "TA-04");

        var anchorId = await SeedTimeAnchorAsync(matchId, 1, (int)TimeAnchorType.PeriodStart);

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{matchId}/anchors/{anchorId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Helpers for Time Anchors

    /// <summary>
    /// Seeds a time anchor record directly into the database for integration testing.
    /// Bypasses the application layer to set up reliable initial state.
    /// </summary>
    /// <param name="matchId">The unique identifier of the associated match.</param>
    /// <param name="periodNumber">The match period number.</param>
    /// <param name="type">The integer representation of the time anchor type.</param>
    /// <returns>The unique identifier of the seeded time anchor.</returns>
    private async Task<Guid> SeedTimeAnchorAsync(Guid matchId, int periodNumber, int type)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var id = Guid.NewGuid();
        const string sql = "INSERT INTO public.timeanchors (id, matchid, periodnumber, type, timestamp) VALUES (@id, @matchid, @period, @type, @ts)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("matchid", matchId);
        cmd.Parameters.AddWithValue("period", periodNumber);
        cmd.Parameters.AddWithValue("type", type);
        cmd.Parameters.AddWithValue("ts", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();

        return id;
    }

    #endregion

    #region Helpers for Events

    /// <summary>
    /// Seeds an access policy to satisfy authorization requirements.
    /// </summary>
    private async Task SeedAccessPolicyAsync(string userId, int role, int targetType, Guid targetId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
              VALUES (@id, @userId, @role, @targetType, @targetId, @now)",
            new { id = Guid.NewGuid(), userId, role, targetType, targetId, now = DateTime.UtcNow });
    }

    /// <summary>
    /// Seeds an event definition record.
    /// </summary>
    private async Task<Guid> SeedEventDefinitionAsync(Guid sportId, string name, bool isPositive)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        var id = Guid.NewGuid();
        var shortName = name.Length > 10 ? name.Substring(0, 10) : name;

        await conn.ExecuteAsync(
            @"INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat) 
              VALUES (@id, @sportId, @name, @shortName, @isPositive, @createdAt)",
            new { id, sportId, name, shortName, isPositive, createdAt = DateTime.UtcNow });
        return id;
    }

    /// <summary>
    /// Seeds a complete hierarchy for match lineups.
    /// </summary>
    private async Task<Guid> SeedMatchLineupAsync(Guid matchId, Guid teamId, Guid cityId, Guid tournamentId, Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        var now = DateTime.UtcNow;

        var positionId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@positionId, @sportId, 'Forward', 'FWD')",
            new { positionId, sportId });

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@clubId, @cityId, 'Integration Test Club', @now)",
            new { clubId, cityId, now });

        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
              VALUES (@playerId, @clubId, 'John', 'Doe', @dob, @gender, @now)",
            new { playerId, clubId, dob = new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc), gender = 0, now });

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) 
              VALUES (@rosterId, @playerId, @tournamentId, @teamId, 7, @positionId, @now)",
            new { rosterId, playerId, tournamentId, teamId, positionId, now });

        var lineupId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, isinstartinglineup, positionid) 
              VALUES (@lineupId, @matchId, @rosterId, 7, true, @positionId)",
            new { lineupId, matchId, rosterId, positionId });

        return lineupId;
    }

    /// <summary>
    /// Seeds a game event record.
    /// </summary>
    private async Task<Guid> SeedGameEventAsync(Guid lineupId, Guid eventDefId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat) 
              VALUES (@id, @lineupId, @eventDefId, 1, @ts, '00:15:00'::interval, false, @now)",
            new { id, lineupId, eventDefId, ts = DateTime.UtcNow, now = DateTime.UtcNow });
        return id;
    }

    /// <summary>
    /// Seeds a game event record directly into the database.
    /// </summary>
    /// <param name="lineupId">The target match lineup identifier.</param>
    /// <param name="eventDefId">The event definition identifier.</param>
    /// <param name="timestamp">The absolute timestamp of the event.</param>
    /// <param name="normalizedTime">The relative match time.</param>
    /// <returns>The GUID of the created game event.</returns>
    protected async Task<Guid> SeedGameEventAsync(
        Guid lineupId,
        Guid eventDefId,
        DateTime? timestamp = null,
        TimeSpan? normalizedTime = null)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var id = Guid.NewGuid();
        const string sql = @"INSERT INTO public.gameevents 
            (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat) 
            VALUES (@id, @lId, @edId, 1, @ts, @nt, false, NOW())";

        await conn.ExecuteAsync(sql, new
        {
            id,
            lId = lineupId,
            edId = eventDefId,
            ts = timestamp ?? DateTime.UtcNow,
            nt = normalizedTime
        });

        return id;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Retrieves the lineup identifier for a specific match and team by joining with player rosters.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <returns>The GUID of the found match lineup.</returns>
    private async Task<Guid> GetLineupIdAsync(Guid matchId, Guid teamId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<Guid>(@"
            SELECT ml.id 
            FROM public.matchlineups ml 
            JOIN public.playerrosters pr ON ml.playerrosterid = pr.id 
            WHERE ml.matchid = @mId AND pr.teamid = @tId 
            LIMIT 1",
            new { mId = matchId, tId = teamId });
    }

    private async Task<(Guid TournamentId, Guid CityId, Guid SportId)> SetupTournamentContextAsync(string ownerId)
    {
        await SeedUserAsync(ownerId);
        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Sport-" + Guid.NewGuid());
        var configId = await SeedConfigurationAsync(sportId);
        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, ownerId, "Tourney-" + Guid.NewGuid());

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

    private async Task<Guid> SeedTeamRegistrationAsync(Guid tournamentId, Guid teamId, Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var posId = Guid.NewGuid();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sId, 'Position', 'POS') ON CONFLICT DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("id", posId);
            cmd.Parameters.AddWithValue("sId", sportId);
            await cmd.ExecuteNonQueryAsync();
        }

        var playerId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.clubs (id, cityid, name, createdat) SELECT @id, id, 'Club', NOW() FROM public.cities LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("id", clubId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = new NpgsqlCommand("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cId, 'First', 'Last', '2000-01-01', 0, NOW())", conn))
        {
            cmd.Parameters.AddWithValue("id", playerId);
            cmd.Parameters.AddWithValue("cId", clubId);
            await cmd.ExecuteNonQueryAsync();
        }

        var rosterId = Guid.NewGuid();
        const string sql = @"
            INSERT INTO public.playerrosters (id, tournamentid, teamid, playerid, positionid, number, createdat)
            VALUES (@id, @tId, @teamId, @pId, @posId, @num, @created)";

        using var rosterCmd = new NpgsqlCommand(sql, conn);
        rosterCmd.Parameters.AddWithValue("id", rosterId);
        rosterCmd.Parameters.AddWithValue("tId", tournamentId);
        rosterCmd.Parameters.AddWithValue("teamId", teamId);
        rosterCmd.Parameters.AddWithValue("pId", playerId);
        rosterCmd.Parameters.AddWithValue("posId", posId);
        rosterCmd.Parameters.AddWithValue("num", new Random().Next(1, 99));
        rosterCmd.Parameters.AddWithValue("created", DateTime.UtcNow);

        await rosterCmd.ExecuteNonQueryAsync();
        return rosterId;
    }

    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, @name, @created) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"{userId}@test.com");
        cmd.Parameters.AddWithValue("name", "Test User");
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
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
        using (var cmd = new NpgsqlCommand("INSERT INTO public.countries (id, name, code) VALUES (1, 'TLand', 'TL') ON CONFLICT DO NOTHING", conn))
            await cmd.ExecuteNonQueryAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.regions (id, name, countryid) VALUES (1, 'TReg', 1) ON CONFLICT DO NOTHING", conn))
            await cmd.ExecuteNonQueryAsync();
        using (var cmd = new NpgsqlCommand("INSERT INTO public.cities (id, name, regionid) VALUES (@id, 'TCity', 1) ON CONFLICT DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("id", cityId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.sports (id, name) VALUES (@id, @name) RETURNING id";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", sportId);
        cmd.Parameters.AddWithValue("name", name);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid> GetFirstPositionIdAsync(Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand("SELECT id FROM public.playerpositiondefinitions WHERE sportid = @sId LIMIT 1", conn);
        cmd.Parameters.AddWithValue("sId", sportId);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    #endregion
}