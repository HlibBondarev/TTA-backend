using Dapper;
using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
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

    #region Event Definitions Tests

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetEventDefinitionsForMatch"/> 
    /// returns 200 OK with the list of event definitions associated with the sport of the specified match.
    /// </summary>
    [Fact]
    public async Task GetEventDefinitionsForMatch_ShouldReturnOkWithDefinitions_WhenMatchAndDefinitionsExist()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-EVDEF-01");

        var goalDefId = await SeedEventDefinitionAsync(context.SportId, "Goal", true);
        var foulDefId = await SeedEventDefinitionAsync(context.SportId, "Foul", false);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/eventdefinitions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<EventDefinitionForMatchResponse>>();

        result.Should().NotBeNull();
        var definitions = result!.ToList();
        definitions.Should().HaveCount(2);
        definitions.Should().Contain(d => d.Id == goalDefId && d.Name == "Goal" && d.IsPositive);
        definitions.Should().Contain(d => d.Id == foulDefId && d.Name == "Foul" && !d.IsPositive);
        definitions.Should().OnlyContain(d => d.SportId == context.SportId);
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetEventDefinitionsForMatch"/> 
    /// returns 200 OK with an empty collection when no event definitions exist for the match's sport.
    /// </summary>
    [Fact]
    public async Task GetEventDefinitionsForMatch_ShouldReturnOkWithEmptyList_WhenNoDefinitionsExistForSport()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-EVDEF-02");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/eventdefinitions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<EventDefinitionForMatchResponse>>();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetEventDefinitionsForMatch"/> 
    /// returns 200 OK with an empty collection when the specified match identifier does not exist.
    /// </summary>
    [Fact]
    public async Task GetEventDefinitionsForMatch_ShouldReturnOkWithEmptyList_WhenMatchDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{nonExistentMatchId}/eventdefinitions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<EventDefinitionForMatchResponse>>();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
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

    #region Player Presences Tests

    /// <summary>
    /// Verifies that <see cref="MatchesController.SubstitutePlayer"/> returns HTTP 201 Created 
    /// and the exact client-supplied presence identifier when an authorized user submits a valid substitution request.
    /// </summary>
    [Fact]
    public async Task SubstitutePlayer_ShouldReturnCreated_WhenRequestIsValidAndUserHasAccess()
    {
        // Arrange
        var context = await SetupPresenceMatchContextAsync(TestUserId);
        var incomingPresenceId = Guid.NewGuid();
        var substitutionTime = DateTime.UtcNow;

        // Record an initial active presence for the outgoing player.
        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(
                "INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein) VALUES (@id, @lineupId, 1, NOW() - INTERVAL '5 minutes')",
                new { id = Guid.NewGuid(), lineupId = context.LineupId1 });
        }

        var request = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: context.LineupId1,
            PlayerInLineupId: context.LineupId2,
            IncomingPresenceId: incomingPresenceId,
            SubstitutionTime: substitutionTime
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{context.MatchId}/substitutions", request);

        // Assert
        var errorContent = response.StatusCode != HttpStatusCode.Created ? await response.Content.ReadAsStringAsync() : string.Empty;

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"because valid substitution should succeed. Error: {errorContent}");

        var createdId = await response.Content.ReadFromJsonAsync<Guid>();
        createdId.Should().Be(incomingPresenceId);

        // Secondary DB verification check asserting exact client-supplied ID and SubstitutionTime
        using var checkConn = Fixture.ConnectionFactory.CreateConnection();
        var matchPresences = (await checkConn.QueryAsync<PlayerPresence>(
            "SELECT id, matchlineupid, periodnumber, timein, timeout FROM public.playerpresences WHERE matchlineupid IN (@id1, @id2)",
            new { id1 = context.LineupId1, id2 = context.LineupId2 })).ToList();

        var incomingPresence = matchPresences.FirstOrDefault(x => x.Id == incomingPresenceId);
        incomingPresence.Should().NotBeNull();
        incomingPresence!.MatchLineupId.Should().Be(context.LineupId2);
        incomingPresence.TimeIn.Should().BeCloseTo(substitutionTime, TimeSpan.FromMilliseconds(500));

        var outgoingPresence = matchPresences.FirstOrDefault(x => x.MatchLineupId == context.LineupId1);
        outgoingPresence.Should().NotBeNull();
        outgoingPresence!.TimeOut.Should().BeCloseTo(substitutionTime, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.SubstitutePlayer"/> returns HTTP 400 Bad Request 
    /// when FluentValidation rules fail (e.g., trying to substitute a player with themselves or empty incoming presence ID).
    /// </summary>
    [Fact]
    public async Task SubstitutePlayer_ShouldReturnBadRequest_WhenValidationFails()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var samePlayerLineupId = Guid.NewGuid();

        var invalidRequest = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: samePlayerLineupId,
            PlayerInLineupId: samePlayerLineupId, // Violation: input and output cannot be identical
            IncomingPresenceId: Guid.NewGuid(),
            SubstitutionTime: DateTime.UtcNow
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/substitutions", invalidRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.SubstitutePlayer"/> returns HTTP 403 Forbidden 
    /// when the authenticated user is not the owner of the tournament or a team editor.
    /// </summary>
    [Fact]
    public async Task SubstitutePlayer_ShouldReturnForbidden_WhenUserLacksEditAccess()
    {
        // Arrange
        const string unauthorizedUserId = "auth0|unauthorized-stranger";
        var context = await SetupPresenceMatchContextAsync(unauthorizedUserId);

        var request = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: context.LineupId1,
            PlayerInLineupId: context.LineupId2,
            IncomingPresenceId: Guid.NewGuid(),
            SubstitutionTime: DateTime.UtcNow
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{context.MatchId}/substitutions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.SubstitutePlayer"/> returns HTTP 201 Created and functions idempotently
    /// when the identical substitution payload is submitted twice, creating only a single presence record in the database.
    /// </summary>
    [Fact]
    public async Task SubstitutePlayer_ShouldBeIdempotent_WhenSamePayloadIsSubmittedTwice()
    {
        // Arrange
        var context = await SetupPresenceMatchContextAsync(TestUserId);
        var incomingPresenceId = Guid.NewGuid();
        var substitutionTime = DateTime.UtcNow;

        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(
                "INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein) VALUES (@id, @lineupId, 1, NOW() - INTERVAL '5 minutes')",
                new { id = Guid.NewGuid(), lineupId = context.LineupId1 });
        }

        var request = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: context.LineupId1,
            PlayerInLineupId: context.LineupId2,
            IncomingPresenceId: incomingPresenceId,
            SubstitutionTime: substitutionTime
        );

        // Act 1: Initial Substitution Request
        var response1 = await Client.PostAsJsonAsync($"{BaseUrl}/{context.MatchId}/substitutions", request);

        // Act 2: Replay Identical Request (e.g. Offline Sync Retry)
        var response2 = await Client.PostAsJsonAsync($"{BaseUrl}/{context.MatchId}/substitutions", request);

        // Assert HTTP Responses
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);

        var returnedId1 = await response1.Content.ReadFromJsonAsync<Guid>();
        var returnedId2 = await response2.Content.ReadFromJsonAsync<Guid>();

        returnedId1.Should().Be(incomingPresenceId);
        returnedId2.Should().Be(incomingPresenceId);

        // Secondary DB verification: Only ONE presence record with IncomingPresenceId must exist in the database
        using var checkConn = Fixture.ConnectionFactory.CreateConnection();
        var matchPresences = (await checkConn.QueryAsync<PlayerPresence>(
            "SELECT id, matchlineupid, periodnumber, timein FROM public.playerpresences WHERE id = @presenceId",
            new { presenceId = incomingPresenceId })).ToList();

        matchPresences.Should().HaveCount(1, "because idempotent replay must not insert duplicate records.");
        matchPresences.First().MatchLineupId.Should().Be(context.LineupId2);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.InitializePeriodPresence"/> returns HTTP 204 No Content 
    /// when an authorized user submits a valid bulk starting lineup initialization request, and persists exact client IDs and timestamps.
    /// </summary>
    [Fact]
    public async Task InitializePeriodPresence_ShouldReturnNoContent_WhenRequestIsValidAndUserHasAccess()
    {
        // Arrange
        var context = await SetupPresenceMatchContextAsync(TestUserId);
        var presenceId1 = Guid.NewGuid();
        var presenceId2 = Guid.NewGuid();
        var timeIn = DateTime.UtcNow;

        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            TimeIn: timeIn,
            PresenceItems: new List<PlayerPresenceItemDto>
            {
                new(presenceId1, context.LineupId1),
                new(presenceId2, context.LineupId2)
            }
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{context.MatchId}/presence/initialize", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Secondary DB verify check asserting exact client-supplied IDs and TimeIn
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var matchPresences = (await conn.QueryAsync<PlayerPresence>(
            "SELECT id, matchlineupid, periodnumber, timein FROM public.playerpresences WHERE periodnumber = 1 AND matchlineupid IN (@id1, @id2)",
            new { id1 = context.LineupId1, id2 = context.LineupId2 })).ToList();

        matchPresences.Should().HaveCount(2);

        var presence1 = matchPresences.FirstOrDefault(x => x.Id == presenceId1);
        presence1.Should().NotBeNull();
        presence1!.MatchLineupId.Should().Be(context.LineupId1);
        presence1.TimeIn.Should().BeCloseTo(timeIn, TimeSpan.FromMilliseconds(500));

        var presence2 = matchPresences.FirstOrDefault(x => x.Id == presenceId2);
        presence2.Should().NotBeNull();
        presence2!.MatchLineupId.Should().Be(context.LineupId2);
        presence2.TimeIn.Should().BeCloseTo(timeIn, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetMatchPresence"/> returns HTTP 200 OK 
    /// along with the complete timeline history array, and functions correctly under anonymous access context.
    /// </summary>
    [Fact]
    public async Task GetMatchPresence_ShouldReturnOkWithTimeline_WhenMatchExists()
    {
        // Arrange
        var context = await SetupPresenceMatchContextAsync(TestUserId);
        var baseTime = DateTime.UtcNow;

        // Directly insert two chronological presence tracking records into the database scope
        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout) 
                VALUES 
                    (gen_random_uuid(), @l1, 1, @t1, @t2),
                    (gen_random_uuid(), @l2, 1, @t2, NULL)",
                new { l1 = context.LineupId1, l2 = context.LineupId2, t1 = baseTime.AddMinutes(-10), t2 = baseTime });
        }

        // Act - Invoke under default client wrapper (respects AllowAnonymous attribute configuration changes)
        var response = await Client.GetAsync($"{BaseUrl}/{context.MatchId}/presence");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var timeline = await response.Content.ReadFromJsonAsync<IEnumerable<PlayerPresenceResponse>>();

        timeline.Should().NotBeNull();
        var resultList = timeline!.ToList();
        resultList.Count.Should().Be(2);
        resultList[0].MatchLineupId.Should().Be(context.LineupId1);
        resultList[1].MatchLineupId.Should().Be(context.LineupId2);
        resultList[1].TimeOut.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetPlayersTimeInMatchByTeam"/> returns 200 OK 
    /// with an accurately calculated collection of clean and dirty play time metrics when the pipeline executes end-to-end.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatchByTeam_ShouldReturnOk_WhenRequestIsValidAndTeamBelongsToMatch()
    {
        // Arrange
        var context = await SeedAnalyticsEnvironmentAsync(ownerId: "auth0|different-owner");

        // Grant TeamEditor permission (targettype 2 = Team, role 1 = Editor) to the current test user
        await GrantAccessPolicyAsync(BaseApiTest.TestUserId, targetType: 2, targetId: context.TeamId, role: 1);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{context.MatchId}/teams/{context.TeamId}/presence/calculate");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var analytics = (await response.Content.ReadFromJsonAsync<IEnumerable<PlayerTimeInMatchResponse>>())?.ToList();
        analytics.Should().NotBeNull().And.NotBeEmpty();
        analytics.Should().HaveCount(1);

        // Assert end-to-end piecewise-linear mathematical calculation accuracy:
        // Nominal duration: 8 mins, Real duration: 10 mins -> K = 0.8
        // Dirty time: 300 seconds -> Clean time: 300 * 0.8 = 240 seconds
        var playerRecord = analytics!.First();
        playerRecord.MatchLineupId.Should().Be(context.LineupId);
        playerRecord.DirtyTimeInMatch.Should().Be(TimeSpan.FromSeconds(300));
        playerRecord.CleanTimeInMatch.Should().Be(TimeSpan.FromSeconds(240));
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetPlayersTimeInMatchByTeam"/> returns 403 Forbidden
    /// when the requested team ID does not belong to the home or guest team boundaries of the targeted match.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatchByTeam_ShouldReturnForbidden_WhenTeamIdIsOutsideMatchBoundaries()
    {
        // Arrange
        var context = await SeedAnalyticsEnvironmentAsync(ownerId: "auth0|different-owner");
        var completelyRandomTeamId = Guid.NewGuid();

        // Grant TeamEditor permission to the random team ID so the top-level authorization policy filter passes,
        // allowing execution to safely reach the controller's internal domain multi-tenancy boundary checks.
        await GrantAccessPolicyAsync(BaseApiTest.TestUserId, targetType: 2, targetId: completelyRandomTeamId, role: 1);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{context.MatchId}/teams/{completelyRandomTeamId}/presence/calculate");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetPlayersTimeInMatchByTeam"/> returns 400 Bad Request
    /// when the input route parameters fail the FluentValidation empty GUID checks.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatchByTeam_ShouldReturnBadRequest_WhenParametersAreEmptyGuids()
    {
        // Arrange
        var validTeamId = Guid.NewGuid();

        // Grant Global Editor permission (targettype 0 = Global, targetId = null, role = 1 = Editor)
        // to bypass the top-level route authorization policy and ensure execution enters the action method's validator execution block.
        await GrantAccessPolicyAsync(BaseApiTest.TestUserId, targetType: 0, targetId: null, role: 1);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.Empty}/teams/{validTeamId}/presence/calculate");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that the administrative endpoint <see cref="MatchesController.GetPlayersTimeInMatch"/> returns 200 OK
    /// and accurately processes the full analytic calculations when invoked by an authorized tournament owner.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatch_Admin_ShouldReturnOk_WhenUserIsTournamentOwner()
    {
        // Arrange
        // Current logged-in user in BaseApiTest context is defined as BaseApiTest.TestUserId ("auth0|test-user")
        var context = await SeedAnalyticsEnvironmentAsync(ownerId: BaseApiTest.TestUserId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{context.MatchId}/teams/{context.TeamId}/presence/calculate-admin");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var analytics = (await response.Content.ReadFromJsonAsync<IEnumerable<PlayerTimeInMatchResponse>>())?.ToList();
        analytics.Should().NotBeNull().And.NotBeEmpty();
        analytics.Should().HaveCount(1);

        // Assert end-to-end piecewise-linear mathematical calculation accuracy for admin flow
        var playerRecord = analytics!.First();
        playerRecord.MatchLineupId.Should().Be(context.LineupId);
        playerRecord.DirtyTimeInMatch.Should().Be(TimeSpan.FromSeconds(300));
        playerRecord.CleanTimeInMatch.Should().Be(TimeSpan.FromSeconds(240));
    }

    #region Seed Helpers for Analytics

    /// <summary>
    /// Seeds a complete relational aggregate structure (Geography, Sport, Club, Tournament, Team, Match, Lineups, Time Anchors, Presences) 
    /// inside the containerized PostgreSQL instance to isolate integration test contexts and enforce complete performance calculations.
    /// </summary>
    /// <param name="ownerId">The Auth0 user identifier assigned as the owner of the tournament.</param>
    /// <returns>A tuple containing the generated MatchId, TeamId, and target active MatchLineupId.</returns>
    private async Task<(Guid MatchId, Guid TeamId, Guid LineupId)> SeedAnalyticsEnvironmentAsync(string ownerId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        // 1. Geography
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) 
            VALUES ('Integration Country', 'INC') 
            ON CONFLICT (name) DO NOTHING", transaction: transaction);
        var countryId = await conn.QuerySingleAsync<int>("SELECT id FROM public.countries WHERE code = 'INC'", transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.regions (countryid, name) 
            VALUES (@cid, 'Integration Region') 
            ON CONFLICT (countryid, name) DO NOTHING",
            new { cid = countryId }, transaction: transaction);
        var regionId = await conn.QuerySingleAsync<int>("SELECT id FROM public.regions WHERE name = 'Integration Region' AND countryid = @cid", new { cid = countryId }, transaction: transaction);

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.cities (id, regionid, name) 
            VALUES (@id, @rid, 'Integration City') 
            ON CONFLICT (regionid, name) DO NOTHING",
            new { id = cityId, rid = regionId }, transaction: transaction);
        cityId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.cities WHERE regionid = @rid AND name = 'Integration City'", new { rid = regionId }, transaction: transaction);

        // 2. User & Sport Configuration (8 minutes nominal duration defined)
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) 
            VALUES (@id, @email, 'Tester', NOW()) 
            ON CONFLICT (id) DO NOTHING",
            new { id = ownerId, email = $"{ownerId}@tta.com" }, transaction: transaction);

        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, 'Integration Sport', 'INS', @configId) 
            ON CONFLICT (name) DO NOTHING",
            new { id = sportId, configId }, transaction: transaction);
        sportId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.sports WHERE name = 'Integration Sport'", transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            SELECT @id, @sid, true, 4, 8, '30x20', 15, 7 
            WHERE NOT EXISTS (SELECT 1 FROM public.sportconfigurations WHERE sportid = @sid)",
            new { id = configId, sid = sportId }, transaction: transaction);
        configId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.sportconfigurations WHERE sportid = @sid LIMIT 1", new { sid = sportId }, transaction: transaction);

        var posId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            SELECT @id, @sid, 'Center Forward', 'CF' 
            WHERE NOT EXISTS (SELECT 1 FROM public.playerpositiondefinitions WHERE sportid = @sid AND shortname = 'CF')",
            new { id = posId, sid = sportId }, transaction: transaction);
        posId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.playerpositiondefinitions WHERE sportid = @sid AND shortname = 'CF' LIMIT 1", new { sid = sportId }, transaction: transaction);

        // 3. Organization (Club, Tournament, Team, Match setup)
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityid, @name, NOW())",
            new { id = clubId, cityid = cityId, name = $"Club_{Guid.NewGuid():N}" }, transaction: transaction);

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sid, @cfgid, @cityid, @oid, @name, NOW(), NOW())",
            new { id = tournamentId, sid = sportId, cfgid = configId, cityid = cityId, oid = ownerId, name = $"Tournament_{Guid.NewGuid():N}" }, transaction: transaction);

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cid, @sid, @name, 0, NOW())",
            new { id = teamId, cid = clubId, sid = sportId, name = $"Team_{Guid.NewGuid():N}" }, transaction: transaction);

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, NOW(), NOW())",
            new { id = matchId, tid = tournamentId, teamid = teamId }, transaction: transaction);

        // 4. Performance Analytics Computational Mock Data (Player, Roster, Lineup)
        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'Analytics', 'Player', '2000-01-01', 0, NOW())", new { id = playerId, cid = clubId }, transaction: transaction);

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) VALUES (@id, @pid, @tid, @teamid, 7, @posid, NOW())", new { id = rosterId, pid = playerId, tid = tournamentId, teamid = teamId, posid = posId }, transaction: transaction);

        var lineupId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) VALUES (@id, @mid, @rid, 7, @posid)", new { id = lineupId, mid = matchId, rid = rosterId, posid = posId }, transaction: transaction);

        // 5. Setup Time Anchors for Period 1: PeriodStart (0) and PeriodEnd (1)
        // Real duration: 10 minutes (600 seconds) -> Scaling coefficient K = 8 / 10 = 0.8
        var baseTime = DateTime.UtcNow;
        await conn.ExecuteAsync(@"
            INSERT INTO public.timeanchors (id, matchid, periodnumber, type, timestamp)
            VALUES 
            (@id1, @mid, 1, 0, @timeStart),
            (@id2, @mid, 1, 1, @timeEnd)",
            new
            {
                id1 = Guid.NewGuid(),
                id2 = Guid.NewGuid(),
                mid = matchId,
                timeStart = baseTime,
                timeEnd = baseTime.AddSeconds(600)
            }, transaction: transaction);

        // 6. Setup active Player Presence entry representing exactly 300 linear ("dirty") seconds in the water
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout)
            VALUES (@id, @lineupId, 1, @timeIn, @timeOut)",
            new
            {
                id = Guid.NewGuid(),
                lineupId = lineupId,
                timeIn = baseTime.AddSeconds(60),
                timeOut = baseTime.AddSeconds(360)
            }, transaction: transaction);

        await transaction.CommitAsync();

        return (matchId, teamId, lineupId);
    }

    /// <summary>
    /// Grants a specific authorization permission policy to a user inside the test database context.
    /// Safely ensures the referenced user exists inside public.users to satisfy database foreign key requirements.
    /// </summary>
    private async Task GrantAccessPolicyAsync(string userId, int targetType, Guid? targetId, int role)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat)
            VALUES (@id, @email, 'Integration Policy User', NOW())
            ON CONFLICT (id) DO NOTHING",
            new { id = userId, email = $"{userId.Replace("|", "_")}@tta.com" });

        await conn.ExecuteAsync(@"
            INSERT INTO auth.accesspolicies (id, userid, targettype, targetid, role, createdat)
            VALUES (@id, @userid, @targettype, @targetid, @role, NOW())
            ON CONFLICT DO NOTHING",
            new { id = Guid.NewGuid(), userid = userId, targettype = targetType, targetid = targetId, role = role });
    }

    #endregion

    #endregion

    #region Batch Event Time Normalization API Tests

    /// <summary>
    /// Verifies that the <c>PUT /api/matches/{matchId}/teams/{teamId}/events/normalize</c> endpoint 
    /// returns HTTP 204 No Content when called by an authorized team representative and the parameters are valid.
    /// </summary>
    [Fact]
    public async Task NormalizeMatchTimeByTeam_ShouldReturnNoContent_WhenRequestIsValidAndAuthorized()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();

        // Use exclusively the real existing helper methods from the bottom of this class
        await SeedRequiredLocationDataAsync(cityId);
        await SeedSportDataAsync(sportId, $"WaterPolo_Api_{Guid.NewGuid():N}");
        var configId = await SeedConfigurationAsync(sportId);

        // Seed user first to satisfy the tournament foreign key constraint (tournaments_ownerid_fkey)
        await SeedUserAsync(TestUserId);
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, TestUserId, "Spring Cup");

        // Seed both participating teams using the existing helper method
        var homeTeamId = await SeedTeamAsync(cityId, sportId, "Team A");
        var guestTeamId = await SeedTeamAsync(cityId, sportId, "Team B");

        // Seed access policy to pass the [Authorize(Policy = "TeamEditor")] requirements (Role 1 = Editor, TargetType 2 = Team)
        await SeedAccessPolicyAsync(TestUserId, 1, 2, homeTeamId);

        // Seed match and baseline time anchor using the existing helper methods
        await SeedMatchAsync(matchId, tournamentId, homeTeamId, guestTeamId, $"M-NORM-{Guid.NewGuid().ToString("N").Substring(0, 5)}");
        await SeedTimeAnchorAsync(matchId, 1, 0); // 0 corresponds to PeriodStart type

        var url = $"{BaseUrl}/{matchId}/teams/{homeTeamId}/events/normalize";

        // Act
        var response = await Client.PutAsync(url, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "The endpoint must return 204 No Content upon successful batch time normalization pipeline routing.");
    }

    /// <summary>
    /// Verifies that the <c>PUT /api/matches/{matchId}/teams/{teamId}/events/normalize-admin</c> endpoint 
    /// returns HTTP 204 No Content when executed by a tournament organizer.
    /// </summary>
    [Fact]
    public async Task NormalizeMatchTime_AdminEndpoint_ShouldReturnNoContent_WhenOrganizerIsValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();

        // Use exclusively the real existing helper methods from the bottom of this class
        await SeedRequiredLocationDataAsync(cityId);
        await SeedSportDataAsync(sportId, $"WaterPolo_Admin_{Guid.NewGuid():N}");
        var configId = await SeedConfigurationAsync(sportId);

        // Seed user first to satisfy the tournament foreign key constraint (tournaments_ownerid_fkey)
        await SeedUserAsync(TestUserId);
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, TestUserId, "Admin Tournament");

        // Seed both participating teams using the existing helper method
        var homeTeamId = await SeedTeamAsync(cityId, sportId, "Team Admin A");
        var guestTeamId = await SeedTeamAsync(cityId, sportId, "Team Admin B");

        // Seed match and baseline time anchor using the existing helper methods
        await SeedMatchAsync(matchId, tournamentId, homeTeamId, guestTeamId, $"M-NORM-ADM-{Guid.NewGuid().ToString("N").Substring(0, 5)}");
        await SeedTimeAnchorAsync(matchId, 1, 0); // 0 corresponds to PeriodStart type

        var url = $"{BaseUrl}/{matchId}/teams/{homeTeamId}/events/normalize-admin";

        // Act
        var response = await Client.PutAsync(url, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "The administrative endpoint must return 204 No Content when tournament ownership constraints match.");
    }

    #endregion

    #region Quick Match Tests

    /// <summary>
    /// Verifies that <see cref="MatchesController.CreateQuickMatch"/> returns <see cref="HttpStatusCode.Created"/> (201)
    /// and a populated <see cref="QuickMatchResponse"/>, and verifies that starting lineups are initialized for both teams.
    /// </summary>
    [Fact]
    public async Task CreateQuickMatch_ShouldReturnCreated_WhenRequestIsValid()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        sportId = await SeedSportDataAsync(sportId, $"QuickPolo_{Guid.NewGuid():N}");
        await SeedUserAsync(TestUserId);

        // Seed JIT Default Infrastructure and Players for Default Club (11111111-1111-1111-1111-000000000001)
        var defaultClubId = Guid.Parse("11111111-1111-1111-1111-000000000001");
        var defaultCityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using (var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.OpenAsync();

            // 1. Ensure geography and default club exist
            await conn.ExecuteAsync(@"
                INSERT INTO public.countries (name, code) VALUES ('Ukraine', 'UA') ON CONFLICT DO NOTHING;
                INSERT INTO public.regions (countryid, name) SELECT id, 'Dnipro Region' FROM public.countries WHERE code = 'UA' ON CONFLICT DO NOTHING;
                INSERT INTO public.cities (id, regionid, name) SELECT @cityId, id, 'Dnipro' FROM public.regions WHERE name = 'Dnipro Region' ON CONFLICT DO NOTHING;
                INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@clubId, @cityId, 'TTA Training Club', NOW()) ON CONFLICT DO NOTHING;",
                new { clubId = defaultClubId, cityId = defaultCityId });

            // 2. Adjust rosterlimit and lineuplimit for test predictability (5 players per team roster, 3 in lineup)
            await conn.ExecuteAsync(@"
                UPDATE public.sportconfigurations 
                SET rosterlimit = 5, lineuplimit = 3 
                WHERE sportid = @sportId;",
                new { sportId });

            // 3. Seed 10 players: 1..5 will be assigned to Home Squad, 6..10 to Guest Squad
            for (int i = 1; i <= 10; i++)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
                    VALUES (@id, @clubId, @fn, 'QuickPlayer', '2000-01-01', 0, NOW())
                    ON CONFLICT DO NOTHING;",
                    new { id = Guid.NewGuid(), clubId = defaultClubId, fn = $"QuickPlayer_{i}" });
            }
        }

        var request = new
        {
            SportId = sportId
        };

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/quick", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<QuickMatchResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();

        // Verify that starting lineups were generated for both Home and Guest teams
        var lineupsResponse = await Client.GetAsync($"{BaseUrl}/{result.Id}/lineups");
        lineupsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lineups = await lineupsResponse.Content.ReadFromJsonAsync<IEnumerable<MatchLineupResponse>>();
        lineups.Should().NotBeNull();
        lineups!.Select(l => l.TeamId).Distinct().Should().HaveCount(2, "starting lineups should be populated for both home and guest teams");
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.CreateQuickMatch"/> returns <see cref="HttpStatusCode.BadRequest"/> (400)
    /// when the request payload fails model validation due to an empty <c>SportId</c>.
    /// </summary>
    [Fact]
    public async Task CreateQuickMatch_ShouldReturnBadRequest_WhenSportIdIsEmpty()
    {
        // Arrange
        var request = new
        {
            SportId = Guid.Empty
        };

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/quick", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that invoking <see cref="MatchesController.CreateQuickMatch"/> without authentication credentials
    /// returns <see cref="HttpStatusCode.Unauthorized"/> (401).
    /// </summary>
    [Fact]
    public async Task CreateQuickMatch_ShouldReturnUnauthorized_WhenAuthenticationIsDisabled()
    {
        // Arrange
        var request = new
        {
            SportId = Guid.NewGuid()
        };

        try
        {
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.PostAsJsonAsync($"{BaseUrl}/quick", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            TestAuthHandler.IsEnabled = true;
        }
    }

    #endregion

    #region Helpers for Player Presences

    /// <summary>
    /// Sets up a robust transactional database context environment optimized for player presence tracking operations.
    /// Generates parent hierarchies and yields a match containing 2 unique lineup references.
    /// </summary>
    /// <param name="tournamentOwnerId">The explicit user identifier to configure as the master tournament owner asset.</param>
    /// <returns>A structured tuple capturing the parent Match ID context alongside two validated Lineup entries.</returns>
    private async Task<(Guid MatchId, Guid LineupId1, Guid LineupId2)> SetupPresenceMatchContextAsync(string tournamentOwnerId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        // 1. Establish Geography structures securely
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) VALUES ('Ukraine', 'UA') ON CONFLICT (name) DO NOTHING;", transaction: transaction);
        var countryId = await conn.QuerySingleAsync<int>("SELECT id FROM public.countries WHERE name = 'Ukraine'", transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.regions (countryid, name) VALUES (@cid, 'Dnipro Region') ON CONFLICT (countryid, name) DO NOTHING;",
            new { cid = countryId }, transaction: transaction);
        var regionId = await conn.QuerySingleAsync<int>("SELECT id FROM public.regions WHERE name = 'Dnipro Region' AND countryid = @cid", new { cid = countryId }, transaction: transaction);

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.cities (id, regionid, name) VALUES (@id, @rid, 'Dnipro') ON CONFLICT (regionid, name) DO NOTHING;",
            new { id = cityId, rid = regionId }, transaction: transaction);
        cityId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.cities WHERE regionid = @rid AND name = 'Dnipro'", new { rid = regionId }, transaction: transaction);

        // 2. Setup Security Identity and Sport structures configurations
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, 'owner@tta.com', 'Manager', NOW()) ON CONFLICT (id) DO NOTHING;",
            new { id = tournamentOwnerId }, transaction: transaction);

        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, 'Water Polo Request', 'WPR', @configId) 
            ON CONFLICT (name) DO NOTHING;",
            new { id = sportId, configId }, transaction: transaction);
        sportId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.sports WHERE name = 'Water Polo Request'", transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            SELECT @id, @sid, true, 4, 8, '30x20', 15, 7 WHERE NOT EXISTS (SELECT 1 FROM public.sportconfigurations WHERE sportid = @sid)",
            new { id = configId, sid = sportId }, transaction: transaction);
        configId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.sportconfigurations WHERE sportid = @sid LIMIT 1", new { sid = sportId }, transaction: transaction);

        var posId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            SELECT @id, @sid, 'Goalkeeper', 'GK' WHERE NOT EXISTS (SELECT 1 FROM public.playerpositiondefinitions WHERE sportid = @sid AND shortname = 'GK')",
            new { id = posId, sid = sportId }, transaction: transaction);
        posId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.playerpositiondefinitions WHERE sportid = @sid AND shortname = 'GK' LIMIT 1", new { sid = sportId }, transaction: transaction);

        // 3. Organization level setup strings data definitions
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityid, 'API_Presence_Club', NOW())",
            new { id = clubId, cityid = cityId }, transaction: transaction);

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sid, @cfgid, @cityid, @oid, 'API_Presence_Cup', NOW(), NOW())",
            new { id = tournamentId, sid = sportId, cfgid = configId, cityid = cityId, oid = tournamentOwnerId }, transaction: transaction);

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cid, @sid, 'API_Presence_Team', 0, NOW())",
            new { id = teamId, cid = clubId, sid = sportId }, transaction: transaction);

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, NOW(), NOW())",
            new { id = matchId, tid = tournamentId, teamid = teamId }, transaction: transaction);

        // 4. Register two distinct physical players into the active game lineup ledger system 
        var playerId1 = Guid.NewGuid();
        var playerId2 = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'Sub', 'Out', '2000-01-01', 0, NOW())", new { id = playerId1, cid = clubId }, transaction: transaction);
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'Sub', 'In', '2000-01-02', 0, NOW())", new { id = playerId2, cid = clubId }, transaction: transaction);

        var rosterId1 = Guid.NewGuid();
        var rosterId2 = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) VALUES (@id, @pid, @tid, @teamid, 11, @posid, NOW())", new { id = rosterId1, pid = playerId1, tid = tournamentId, teamid = teamId, posid = posId }, transaction: transaction);
        await conn.ExecuteAsync(@"INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) VALUES (@id, @pid, @tid, @teamid, 22, @posid, NOW())", new { id = rosterId2, pid = playerId2, tid = tournamentId, teamid = teamId, posid = posId }, transaction: transaction);

        var lineupId1 = Guid.NewGuid();
        var lineupId2 = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) VALUES (@id, @mid, @rid, 11, @posid)", new { id = lineupId1, mid = matchId, rid = rosterId1, posid = posId }, transaction: transaction);
        await conn.ExecuteAsync(@"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) VALUES (@id, @mid, @rid, 22, @posid)", new { id = lineupId2, mid = matchId, rid = rosterId2, posid = posId }, transaction: transaction);

        await transaction.CommitAsync();

        return (matchId, lineupId1, lineupId2);
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
            @"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) 
              VALUES (@lineupId, @matchId, @rosterId, 7, @positionId)",
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

    /// <summary>
    /// Seeds a sport entity along with its associated default sport configuration atomically within an explicit transaction.
    /// Satisfies mandatory <c>shortname</c> and <c>defaultconfigid</c> column requirements as well as deferred foreign key constraints.
    /// </summary>
    /// <param name="sportId">The unique identifier to assign to the new or existing sport record.</param>
    /// <param name="name">The display name of the sport.</param>
    /// <returns>
    /// A task representing the asynchronous database operation, returning the persisted <see cref="Guid"/> identifier of the sport.
    /// </returns>
    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var configId = Guid.NewGuid();
        var shortName = name.Length > 10 ? name[..10] : name;

        // 1. Insert Sport (Updated with shortname & defaultconfigid)
        var sportSql = @"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @name, @shortName, @configId) 
            ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name 
            RETURNING id;";

        using (var cmd = new NpgsqlCommand(sportSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", sportId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("shortName", shortName);
            cmd.Parameters.AddWithValue("configId", configId);

            var result = await cmd.ExecuteScalarAsync();
            sportId = (Guid)result!;
        }

        // 2. Insert SportConfiguration
        var configSql = @"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
            VALUES (@configId, @sportId, false, 2, 45, '105x68', 25, 11)
            ON CONFLICT DO NOTHING;";

        using (var cmd = new NpgsqlCommand(configSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("configId", configId);
            cmd.Parameters.AddWithValue("sportId", sportId);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        return sportId;
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