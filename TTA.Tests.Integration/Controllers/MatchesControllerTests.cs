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
/// Validates match retrieval, lineup management, result recording, quick match provisioning, and reports.
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
    /// Verifies that team match lineup can be retrieved by any user via team-filtered route.
    /// </summary>
    [Fact]
    public async Task GetTeamMatchLineup_ShouldReturnOk_WhenMatchAndTeamExist()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");
        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-LINEUP-1");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/teams/{homeId}/lineup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<MatchLineupResponse>>();
        result.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetTeamMatchLineup"/> returns HTTP 404 Not Found
    /// when the specified team is not a participant in the match.
    /// </summary>
    [Fact]
    public async Task GetTeamMatchLineup_ShouldReturnNotFound_WhenTeamIsNotParticipant()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");
        var externalTeamId = await SeedTeamAsync(context.CityId, context.SportId, "External FC");
        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-LINEUP-2");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/teams/{externalTeamId}/lineup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTeamSummaryReport_ShouldReturnOk_WhenDataExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");
        var matchId = Guid.NewGuid();
        // Передаємо рахунок для фіналізації матчу
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-SUM-REPORT", homeScore: 2, guestScore: 1);

        await SeedMatchLineupAsync(matchId, homeId, context.CityId, context.TournamentId, context.SportId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/teams/{homeId}/reports/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<TeamMatchSummaryReportResponse>>();
        result.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetPlayerDetailedReport"/> returns HTTP 200 OK
    /// and ensures events are strictly sorted in ascending order by EventTimestamp, even when cross-period NormalizedMatchTime conflicts.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailedReport_ShouldReturnOk_WhenDataExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");
        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-DET-REPORT", homeScore: 3, guestScore: 0);

        var lineupId = await SeedMatchLineupAsync(matchId, homeId, context.CityId, context.TournamentId, context.SportId);

        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Seed a valid event definition with mandatory shortname included
        var eventDefId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat)
            VALUES (@id, @sportId, 'Goal', 'GL', true, NOW())",
            new { id = eventDefId, sportId = context.SportId });

        var baseTime = DateTime.UtcNow;

        // Event 1: Period 2, occurs LATER in real time (baseTime + 15 min), but has a SMALLER normalized relative match time (5 min)
        var eventPeriod2 = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat)
            VALUES (@id, @mlId, @edId, 2, @ts, @norm, false, NOW())",
            new { id = eventPeriod2, mlId = lineupId, edId = eventDefId, ts = baseTime.AddMinutes(15), norm = TimeSpan.FromMinutes(5) });

        // Event 2: Period 1, occurs EARLIER in real time (baseTime), but has a LARGER normalized relative match time (40 min)
        var eventPeriod1 = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, normalizedmatchtime, isleadtogoal, createdat)
            VALUES (@id, @mlId, @edId, 1, @ts, @norm, true, NOW())",
            new { id = eventPeriod1, mlId = lineupId, edId = eventDefId, ts = baseTime, norm = TimeSpan.FromMinutes(40) });

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/lineups/{lineupId}/reports/detailed");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PlayerDetailedMatchReportResponse>();
        result.Should().NotBeNull();

        var events = result!.Events.ToList();
        events.Should().HaveCount(2);

        // Verify events are strictly ordered by ascending EventTimestamp (Event 2 from Period 1 first, Event 1 from Period 2 second)
        events[0].EventTimestamp.Should().BeCloseTo(baseTime, TimeSpan.FromSeconds(1));
        events[0].IsLeadToGoal.Should().BeTrue();

        events[1].EventTimestamp.Should().BeCloseTo(baseTime.AddMinutes(15), TimeSpan.FromSeconds(1));
        events[1].IsLeadToGoal.Should().BeFalse();
    }

    [Fact]
    public async Task GetPlayerDetailedReport_ShouldReturnNotFound_WhenMatchIsNotFinalized()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");
        var unfinalizedMatchId = Guid.NewGuid();
        // Створюємо нефіналізований матч (без рахунку)
        await SeedMatchAsync(unfinalizedMatchId, context.TournamentId, homeId, guestId, "M-UNFIN-DET");
        var lineupId = await SeedMatchLineupAsync(unfinalizedMatchId, homeId, context.CityId, context.TournamentId, context.SportId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{unfinalizedMatchId}/lineups/{lineupId}/reports/detailed");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetTeamSummaryReport"/> returns HTTP 404 Not Found
    /// when the match or team does not exist.
    /// </summary>
    [Fact]
    public async Task GetTeamSummaryReport_ShouldReturnNotFound_WhenMatchOrTeamDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();
        var nonExistentTeamId = Guid.NewGuid();

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{nonExistentMatchId}/teams/{nonExistentTeamId}/reports/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetPlayerDetailedReport"/> returns HTTP 404 Not Found
    /// when the match or lineup entry does not exist.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailedReport_ShouldReturnNotFound_WhenMatchOrLineupDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();
        var nonExistentLineupId = Guid.NewGuid();

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{nonExistentMatchId}/lineups/{nonExistentLineupId}/reports/detailed");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.MatchesController.GetTeamSummaryReport"/> returns HTTP 404 Not Found
    /// when requested for an existing match that has not been finalized yet.
    /// </summary>
    [Fact]
    public async Task GetTeamSummaryReport_ShouldReturnNotFound_WhenMatchIsNotFinalized()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC");
        var unfinalizedMatchId = Guid.NewGuid();
        await SeedMatchAsync(unfinalizedMatchId, context.TournamentId, homeId, guestId, "M-UNFIN-SUM");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{unfinalizedMatchId}/teams/{homeId}/reports/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    /// </summary>
    [Fact]
    public async Task CopyFromRoster_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);

        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityId, 'Test Club', now())",
            new { id = clubId, cityId = context.CityId });

        var positionId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sId, 'Forward', 'FW')",
            new { id = positionId, sId = context.SportId });

        var teamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, teamId, guestId, "M-301");

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
    /// </summary>
    [Fact]
    public async Task CopyFromRoster_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var context = await SetupTournamentContextAsync("not-the-owner-id");
        var teamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, teamId, guestId, "M-302");

        var request = new CopyTeamRosterToMatchLineupRequest(new List<Guid> { Guid.NewGuid() });
        var url = $"{BaseUrl}/{matchId}/teams/{teamId}/lineup/copy";

        // Act
        var response = await Client.PostAsJsonAsync(url, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that a user with the TeamEditor role can successfully copy selected players to the match lineup.
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

        await conn.ExecuteAsync(@"
            INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @uId, 1, 2, @tId, now())",
            new { id = Guid.NewGuid(), uId = TestUserId, tId = teamId });

        var positionId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            VALUES (@id, @sId, 'Universal Player', 'UP')",
            new { id = positionId, sId = context.SportId });

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityId, 'Editor Club', now())",
            new { id = clubId, cityId = context.CityId });

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, teamId, guestId, "M-601");

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
        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-SORT-01");

        await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);

        var lineupId = await GetLineupIdAsync(matchId, homeTeamId);
        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Goal", true);

        await SeedGameEventAsync(lineupId, eventDefId, DateTime.UtcNow.AddMinutes(-5), TimeSpan.FromMinutes(40));
        await SeedGameEventAsync(lineupId, eventDefId, DateTime.UtcNow.AddMinutes(-15), TimeSpan.FromMinutes(10));

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await response.Content.ReadFromJsonAsync<List<GameEventResponse>>();
        events.Should().NotBeNull();
        events.Should().HaveCount(2);

        events![0].NormalizedMatchTime.Should().Be(TimeSpan.FromMinutes(10));
        events[1].NormalizedMatchTime.Should().Be(TimeSpan.FromMinutes(40));
    }

    /// <summary>
    /// Verifies that new game events can be recorded in a batch for a specific match.
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

        var request1 = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: lineupId,
            EventDefinitionId: eventDefId,
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false
        );

        var request2 = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: lineupId,
            EventDefinitionId: eventDefId,
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow.AddSeconds(30),
            IsLeadToGoal: true
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/events", new[] { request1, request2 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var returnedIds = await response.Content.ReadFromJsonAsync<IEnumerable<Guid>>();
        returnedIds.Should().NotBeNull();
        returnedIds.Should().BeEquivalentTo([request1.Id, request2.Id]);
    }

    /// <summary>
    /// Verifies that RecordMatchEvent returns 400 Bad Request when the JSON request payload is null.
    /// </summary>
    [Fact]
    public async Task RecordMatchEvent_ShouldReturnBadRequest_WhenPayloadIsNull()
    {
        // Arrange
        var matchId = Guid.NewGuid();

        // Act
        var response = await Client.PostAsJsonAsync<IEnumerable<CreateGameEventRequest>>($"{BaseUrl}/{matchId}/events", null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies recording events in a batch specifically for a team side within a match.
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
        await SeedAccessPolicyAsync(TestUserId, 0, 2, homeTeamId);

        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Timeout", true);
        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);

        var request1 = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: lineupId,
            EventDefinitionId: eventDefId,
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false
        );

        var request2 = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: lineupId,
            EventDefinitionId: eventDefId,
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow.AddSeconds(15),
            IsLeadToGoal: false
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/teams/{homeTeamId}/events", new[] { request1, request2 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var returnedIds = await response.Content.ReadFromJsonAsync<IEnumerable<Guid>>();
        returnedIds.Should().NotBeNull();
        returnedIds.Should().BeEquivalentTo([request1.Id, request2.Id]);
    }

    /// <summary>
    /// Verifies that RecordMatchEventByTeam returns 400 Bad Request when the JSON request payload is null.
    /// </summary>
    [Fact]
    public async Task RecordMatchEventByTeam_ShouldReturnBadRequest_WhenPayloadIsNull()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-NULL-TEST");
        await SeedAccessPolicyAsync(TestUserId, 0, 2, homeTeamId);

        // Act
        var response = await Client.PostAsJsonAsync<IEnumerable<CreateGameEventRequest>>($"{BaseUrl}/{matchId}/teams/{homeTeamId}/events", null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that an existing game event can be updated.
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
    /// </summary>
    [Fact]
    public async Task DeleteMatchEvent_ShouldReturnNoContent_WhenEventExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Home Team");
        var guestTeamId = await SeedTeamAsync(context.CityId, context.SportId, "Guest Team");
        var matchId = Guid.NewGuid();

        await SeedMatchAsync(matchId, context.TournamentId, homeTeamId, guestTeamId, "M-06");
        await SeedAccessPolicyAsync(TestUserId, 0, 2, homeTeamId);

        var eventDefId = await SeedEventDefinitionAsync(context.SportId, "Technical Foul", false);
        var lineupId = await SeedMatchLineupAsync(matchId, homeTeamId, context.CityId, context.TournamentId, context.SportId);
        var eventId = await SeedGameEventAsync(lineupId, eventDefId);

        // Act
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

        await SeedTimeAnchorAsync(matchId, 1, (int)TimeAnchorType.PeriodStart);
        await SeedTimeAnchorAsync(matchId, 1, (int)TimeAnchorType.PeriodEnd);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{matchId}/anchors");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

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

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var anchor = await response.Content.ReadFromJsonAsync<TimeAnchorResponse>(jsonOptions);
        anchor.Should().NotBeNull();
        anchor!.Id.Should().Be(anchorId);
        anchor.MatchId.Should().Be(matchId);
    }

    /// <summary>
    /// Verifies that an authorized user (Tournament Owner) can successfully record a batch of new time anchors.
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

        var anchorId1 = Guid.NewGuid();
        var anchorId2 = Guid.NewGuid();
        var request1 = new CreateTimeAnchorRequest(anchorId1, 1, TimeAnchorType.PeriodStart, DateTime.UtcNow.AddMinutes(-10));
        var request2 = new CreateTimeAnchorRequest(anchorId2, 1, TimeAnchorType.PeriodEnd, DateTime.UtcNow.AddMinutes(-2));

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/anchors", new[] { request1, request2 });

        // Assert
        if (response.StatusCode != HttpStatusCode.Created)
            _output.WriteLine(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var returnedIds = await response.Content.ReadFromJsonAsync<IEnumerable<Guid>>();
        returnedIds.Should().NotBeNull();
        returnedIds.Should().BeEquivalentTo([anchorId1, anchorId2]);
    }

    /// <summary>
    /// Verifies that RecordTimeAnchor returns 400 Bad Request when the JSON request payload is null.
    /// </summary>
    [Fact]
    public async Task RecordTimeAnchor_ShouldReturnBadRequest_WhenPayloadIsNull()
    {
        // Arrange
        var matchId = Guid.NewGuid();

        // Act
        var response = await Client.PostAsJsonAsync<IEnumerable<CreateTimeAnchorRequest>>($"{BaseUrl}/{matchId}/anchors", null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that RecordTimeAnchor returns 400 Bad Request and persists no anchors when any item in the batch is invalid.
    /// </summary>
    [Fact]
    public async Task RecordTimeAnchor_ShouldReturnBadRequest_AndNotPersist_WhenBatchContainsInvalidItem()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC InvAnchor");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC InvAnchor");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "TA-05");

        var validAnchorId = Guid.NewGuid();
        var invalidAnchorId = Guid.NewGuid();

        var validRequest = new CreateTimeAnchorRequest(validAnchorId, 1, TimeAnchorType.PeriodStart, DateTime.UtcNow.AddMinutes(-5));
        var invalidRequest = new CreateTimeAnchorRequest(invalidAnchorId, 0, TimeAnchorType.PeriodEnd, DateTime.UtcNow); // Invalid PeriodNumber = 0

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/anchors", new[] { validRequest, invalidRequest });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify neither anchor was persisted
        var getAnchorsResponse = await Client.GetAsync($"{BaseUrl}/{matchId}/anchors");
        getAnchorsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var anchors = await getAnchorsResponse.Content.ReadFromJsonAsync<List<TimeAnchorResponse>>();
        anchors.Should().NotBeNull();
        anchors.Should().NotContain(a => a.Id == validAnchorId || a.Id == invalidAnchorId);
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
    /// when FluentValidation rules fail.
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
            PlayerInLineupId: samePlayerLineupId,
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
    /// when the authenticated user is not authorized.
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
    /// Verifies that <see cref="MatchesController.SubstitutePlayer"/> functions idempotently when replayed.
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

        // Act
        var response1 = await Client.PostAsJsonAsync($"{BaseUrl}/{context.MatchId}/substitutions", request);
        var response2 = await Client.PostAsJsonAsync($"{BaseUrl}/{context.MatchId}/substitutions", request);

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);

        var returnedId1 = await response1.Content.ReadFromJsonAsync<Guid>();
        var returnedId2 = await response2.Content.ReadFromJsonAsync<Guid>();

        returnedId1.Should().Be(incomingPresenceId);
        returnedId2.Should().Be(incomingPresenceId);

        using var checkConn = Fixture.ConnectionFactory.CreateConnection();
        var matchPresences = (await checkConn.QueryAsync<PlayerPresence>(
            "SELECT id, matchlineupid, periodnumber, timein FROM public.playerpresences WHERE id = @presenceId",
            new { presenceId = incomingPresenceId })).ToList();

        matchPresences.Should().HaveCount(1, "because idempotent replay must not insert duplicate records.");
        matchPresences.First().MatchLineupId.Should().Be(context.LineupId2);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.InitializePeriodPresence"/> returns HTTP 204 No Content.
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
    /// Verifies that <see cref="MatchesController.TerminatePeriodPresence"/> returns HTTP 204 No Content
    /// and sets TimeOut on specified active player presences for the period while leaving unselected active presences open.
    /// </summary>
    [Fact]
    public async Task TerminatePeriodPresence_ShouldReturnNoContent_WhenRequestIsValidAndUserHasAccess()
    {
        // Arrange
        var context = await SetupPresenceMatchContextAsync(TestUserId);
        var timeOut = DateTime.UtcNow;

        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein) 
                VALUES 
                    (@id1, @lineupId1, 1, NOW() - INTERVAL '5 minutes'),
                    (@id2, @lineupId2, 1, NOW() - INTERVAL '5 minutes')",
                new
                {
                    id1 = Guid.NewGuid(),
                    lineupId1 = context.LineupId1,
                    id2 = Guid.NewGuid(),
                    lineupId2 = context.LineupId2
                });
        }

        var request = new TerminatePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: [context.LineupId1],
            TimeOut: timeOut
        );

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{context.MatchId}/presence/terminate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var checkConn = Fixture.ConnectionFactory.CreateConnection();
        var matchPresences = (await checkConn.QueryAsync<PlayerPresence>(
            "SELECT id, matchlineupid, periodnumber, timein, timeout FROM public.playerpresences WHERE matchlineupid IN (@id1, @id2)",
            new { id1 = context.LineupId1, id2 = context.LineupId2 })).ToList();

        matchPresences.Should().HaveCount(2);

        var presence1 = matchPresences.FirstOrDefault(x => x.MatchLineupId == context.LineupId1);
        presence1.Should().NotBeNull();
        presence1!.TimeOut.Should().NotBeNull();
        presence1.TimeOut!.Value.Should().BeCloseTo(timeOut, TimeSpan.FromMilliseconds(500));

        var presence2 = matchPresences.FirstOrDefault(x => x.MatchLineupId == context.LineupId2);
        presence2.Should().NotBeNull();
        presence2!.TimeOut.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.TerminatePeriodPresence"/> returns HTTP 409 Conflict
    /// when the provided TimeOut timestamp is earlier than the active presence session's TimeIn timestamp.
    /// </summary>
    [Fact]
    public async Task TerminatePeriodPresence_ShouldReturnConflict_WhenTimeOutIsBeforeTimeIn()
    {
        // Arrange
        var context = await SetupPresenceMatchContextAsync(TestUserId);
        var exactTimeIn = DateTime.UtcNow;
        var invalidTimeOut = exactTimeIn.AddMinutes(-10); // TimeOut earlier than TimeIn

        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(
                "INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein) VALUES (@id, @lineupId, 1, @timeIn)",
                new { id = Guid.NewGuid(), lineupId = context.LineupId1, timeIn = exactTimeIn });
        }

        var request = new TerminatePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: new[] { context.LineupId1 },
            TimeOut: invalidTimeOut
        );

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{context.MatchId}/presence/terminate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetMatchPresence"/> returns HTTP 200 OK along with timeline history.
    /// </summary>
    [Fact]
    public async Task GetMatchPresence_ShouldReturnOkWithTimeline_WhenMatchExists()
    {
        // Arrange
        var context = await SetupPresenceMatchContextAsync(TestUserId);
        var baseTime = DateTime.UtcNow;

        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout) 
                VALUES 
                    (gen_random_uuid(), @l1, 1, @t1, @t2),
                    (gen_random_uuid(), @l2, 1, @t2, NULL)",
                new { l1 = context.LineupId1, l2 = context.LineupId2, t1 = baseTime.AddMinutes(-10), t2 = baseTime });
        }

        // Act
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
    /// Verifies that <see cref="MatchesController.GetPlayersTimeInMatchByTeam"/> returns 200 OK with calculated metrics.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatchByTeam_ShouldReturnOk_WhenRequestIsValidAndTeamBelongsToMatch()
    {
        // Arrange
        var context = await SeedAnalyticsEnvironmentAsync(ownerId: "auth0|different-owner");

        await GrantAccessPolicyAsync(BaseApiTest.TestUserId, targetType: 2, targetId: context.TeamId, role: 1);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{context.MatchId}/teams/{context.TeamId}/presence/calculate");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var analytics = (await response.Content.ReadFromJsonAsync<IEnumerable<PlayerTimeInMatchResponse>>())?.ToList();
        analytics.Should().NotBeNull().And.NotBeEmpty();
        analytics.Should().HaveCount(1);

        var playerRecord = analytics!.First();
        playerRecord.MatchLineupId.Should().Be(context.LineupId);
        playerRecord.DirtyTimeInMatch.Should().Be(TimeSpan.FromSeconds(300));
        playerRecord.CleanTimeInMatch.Should().Be(TimeSpan.FromSeconds(240));
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetPlayersTimeInMatchByTeam"/> returns 403 Forbidden
    /// when the team is outside match boundaries.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatchByTeam_ShouldReturnForbidden_WhenTeamIdIsOutsideMatchBoundaries()
    {
        // Arrange
        var context = await SeedAnalyticsEnvironmentAsync(ownerId: "auth0|different-owner");
        var completelyRandomTeamId = Guid.NewGuid();

        await GrantAccessPolicyAsync(BaseApiTest.TestUserId, targetType: 2, targetId: completelyRandomTeamId, role: 1);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{context.MatchId}/teams/{completelyRandomTeamId}/presence/calculate");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetPlayersTimeInMatchByTeam"/> returns 400 Bad Request on empty GUID.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatchByTeam_ShouldReturnBadRequest_WhenParametersAreEmptyGuids()
    {
        // Arrange
        var validTeamId = Guid.NewGuid();

        await GrantAccessPolicyAsync(BaseApiTest.TestUserId, targetType: 0, targetId: null, role: 1);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.Empty}/teams/{validTeamId}/presence/calculate");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that the administrative endpoint <see cref="MatchesController.GetPlayersTimeInMatch"/> returns 200 OK.
    /// </summary>
    [Fact]
    public async Task GetPlayersTimeInMatch_Admin_ShouldReturnOk_WhenUserIsTournamentOwner()
    {
        // Arrange
        var context = await SeedAnalyticsEnvironmentAsync(ownerId: BaseApiTest.TestUserId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{context.MatchId}/teams/{context.TeamId}/presence/calculate-admin");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var analytics = (await response.Content.ReadFromJsonAsync<IEnumerable<PlayerTimeInMatchResponse>>())?.ToList();
        analytics.Should().NotBeNull().And.NotBeEmpty();
        analytics.Should().HaveCount(1);

        var playerRecord = analytics!.First();
        playerRecord.MatchLineupId.Should().Be(context.LineupId);
        playerRecord.DirtyTimeInMatch.Should().Be(TimeSpan.FromSeconds(300));
        playerRecord.CleanTimeInMatch.Should().Be(TimeSpan.FromSeconds(240));
    }

    #endregion

    #region Batch Event Time Normalization API Tests

    /// <summary>
    /// Verifies that <c>PUT /api/matches/{matchId}/teams/{teamId}/events/normalize</c> returns 204 No Content.
    /// </summary>
    [Fact]
    public async Task NormalizeMatchTimeByTeam_ShouldReturnNoContent_WhenRequestIsValidAndAuthorized()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();

        await SeedRequiredLocationDataAsync(cityId);
        await SeedSportDataAsync(sportId, $"WaterPolo_Api_{Guid.NewGuid():N}");
        var configId = await SeedConfigurationAsync(sportId);

        await SeedUserAsync(TestUserId, "test@example.com", "Test User");
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, TestUserId, "Spring Cup");

        var homeTeamId = await SeedTeamAsync(cityId, sportId, "Team A");
        var guestTeamId = await SeedTeamAsync(cityId, sportId, "Team B");

        await SeedAccessPolicyAsync(TestUserId, 1, 2, homeTeamId);

        await SeedMatchAsync(matchId, tournamentId, homeTeamId, guestTeamId, $"M-NORM-{Guid.NewGuid().ToString("N").Substring(0, 5)}");
        await SeedTimeAnchorAsync(matchId, 1, 0);

        var url = $"{BaseUrl}/{matchId}/teams/{homeTeamId}/events/normalize";

        // Act
        var response = await Client.PutAsync(url, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Verifies that <c>PUT /api/matches/{matchId}/teams/{teamId}/events/normalize-admin</c> returns 204 No Content.
    /// </summary>
    [Fact]
    public async Task NormalizeMatchTime_AdminEndpoint_ShouldReturnNoContent_WhenOrganizerIsValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();

        await SeedRequiredLocationDataAsync(cityId);
        await SeedSportDataAsync(sportId, $"WaterPolo_Admin_{Guid.NewGuid():N}");
        var configId = await SeedConfigurationAsync(sportId);

        await SeedUserAsync(TestUserId, "test@example.com", "Test User");
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, TestUserId, "Admin Tournament");

        var homeTeamId = await SeedTeamAsync(cityId, sportId, "Team Admin A");
        var guestTeamId = await SeedTeamAsync(cityId, sportId, "Team Admin B");

        await SeedMatchAsync(matchId, tournamentId, homeTeamId, guestTeamId, $"M-NORM-ADM-{Guid.NewGuid().ToString("N").Substring(0, 5)}");
        await SeedTimeAnchorAsync(matchId, 1, 0);

        var url = $"{BaseUrl}/{matchId}/teams/{homeTeamId}/events/normalize-admin";

        // Act
        var response = await Client.PutAsync(url, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Quick Match Tests

    /// <summary>
    /// Verifies that <see cref="MatchesController.CreateQuickMatch"/> returns <see cref="HttpStatusCode.Created"/> (201),
    /// a populated <see cref="QuickMatchResponse"/>, Provisions JIT User in public.users, verifies tournament ownership,
    /// and initializes starting lineups for both Home and Guest teams.
    /// </summary>
    [Fact]
    public async Task CreateQuickMatch_ShouldReturnCreated_WhenRequestIsValid()
    {
        // Arrange
        var jitUserId = $"auth0|jit-{Guid.NewGuid():N}";
        var sportId = Guid.NewGuid();
        sportId = await SeedSportDataAsync(sportId, $"QuickPolo_{Guid.NewGuid():N}");

        var defaultClubId = Guid.Parse("11111111-1111-1111-1111-000000000001");
        var tempCityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using (var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.OpenAsync();

            // 1. Ensure geography exists
            await conn.ExecuteAsync(@"
                INSERT INTO public.countries (name, code) VALUES ('Ukraine', 'UA') ON CONFLICT DO NOTHING;
                INSERT INTO public.regions (countryid, name) SELECT id, 'Dnipro Region' FROM public.countries WHERE code = 'UA' ON CONFLICT DO NOTHING;
                INSERT INTO public.cities (id, regionid, name) SELECT @cityId, id, 'Dnipro' FROM public.regions WHERE name = 'Dnipro Region' ON CONFLICT DO NOTHING;",
                new { cityId = tempCityId });

            var actualCityId = await conn.QuerySingleAsync<Guid>(
                "SELECT id FROM public.cities WHERE name = 'Dnipro' LIMIT 1");

            // 2. Ensure default club exists using the resolved city ID
            await conn.ExecuteAsync(@"
                INSERT INTO public.clubs (id, cityid, name, createdat) 
                VALUES (@clubId, @cityId, 'TTA Training Club', NOW()) 
                ON CONFLICT DO NOTHING;",
                new { clubId = defaultClubId, cityId = actualCityId });

            // 3. Adjust rosterlimit and lineuplimit for test predictability (5 players per team roster, 3 in lineup)
            await conn.ExecuteAsync(@"
                UPDATE public.sportconfigurations 
                SET rosterlimit = 5, lineuplimit = 3 
                WHERE sportid = @sportId;",
                new { sportId });

            // 4. Seed 10 players: 1..5 will be assigned to Home Squad, 6..10 to Guest Squad
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

        try
        {
            TestAuthHandler.CustomUserId = jitUserId;

            // Act
            var response = await Client.PostAsJsonAsync($"{BaseUrl}/quick", request);

            // Assert 1: HTTP Response Status and Location Header
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.Location.Should().NotBeNull();

            var result = await response.Content.ReadFromJsonAsync<QuickMatchResponse>();
            result.Should().NotBeNull();
            result!.Id.Should().NotBeEmpty();
            result.HomeTeamId.Should().NotBeEmpty();
            result.GuestTeamId.Should().NotBeEmpty();
            result.TournamentId.Should().NotBeEmpty();

            // Assert 2: Database State Verification - JIT User Provisioning in public.users
            using (var checkConn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection())
            {
                await checkConn.OpenAsync();

                var jitUser = await checkConn.QueryFirstOrDefaultAsync<User>(
                    "SELECT id, email, displayname FROM public.users WHERE id = @userId",
                    new { userId = jitUserId });

                jitUser.Should().NotBeNull("JIT user record must be provisioned in public.users");

                // Assert 3: Database State Verification - Tournament Ownership
                var tournament = await checkConn.QueryFirstOrDefaultAsync<Tournament>(
                    "SELECT id, ownerid FROM public.tournaments WHERE id = @tournamentId",
                    new { tournamentId = result.TournamentId });

                tournament.Should().NotBeNull("Tournament record must be created in public.tournaments");
                tournament!.OwnerId.Should().Be(jitUserId, "Tournament owner must be set to the creator user ID");
            }

            // Assert 4: Verify starting lineups were generated for both Home and Guest teams
            var homeLineupResponse = await Client.GetAsync($"{BaseUrl}/{result.Id}/teams/{result.HomeTeamId}/lineup");
            homeLineupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var homeLineup = (await homeLineupResponse.Content.ReadFromJsonAsync<IEnumerable<MatchLineupResponse>>())?.ToList();
            homeLineup.Should().NotBeNull();
            homeLineup!.Should().HaveCount(3, "home team lineup should contain 3 players based on lineuplimit=3");
            homeLineup.Should().OnlyContain(l => l.TeamId == result.HomeTeamId, "all home lineup items must belong to HomeTeamId");

            var guestLineupResponse = await Client.GetAsync($"{BaseUrl}/{result.Id}/teams/{result.GuestTeamId}/lineup");
            guestLineupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var guestLineup = (await guestLineupResponse.Content.ReadFromJsonAsync<IEnumerable<MatchLineupResponse>>())?.ToList();
            guestLineup.Should().NotBeNull();
            guestLineup!.Should().HaveCount(3, "guest team lineup should contain 3 players based on lineuplimit=3");
            guestLineup.Should().OnlyContain(l => l.TeamId == result.GuestTeamId, "all guest lineup items must belong to GuestTeamId");
        }
        finally
        {
            TestAuthHandler.CustomUserId = null;
        }
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

    /// <summary>
    /// Verifies that <see cref="MatchesController.CreateQuickMatch"/> returns <see cref="HttpStatusCode.Unauthorized"/> (401)
    /// when the authenticated user's email claim is missing or empty.
    /// </summary>
    [Fact]
    public async Task CreateQuickMatch_ShouldReturnUnauthorized_WhenUserEmailClaimIsMissing()
    {
        // Arrange
        var request = new
        {
            SportId = Guid.NewGuid()
        };

        try
        {
            // Omit the email claim to trigger the missing userEmail validation check
            TestAuthHandler.CustomEmail = string.Empty;

            // Act
            var response = await Client.PostAsJsonAsync($"{BaseUrl}/quick", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("Valid user identification claims are required.");
        }
        finally
        {
            TestAuthHandler.CustomEmail = null;
        }
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.CreateQuickMatch"/> falls back to using the user's email as display name
    /// when the display name claim is null or whitespace, and persists it to public.users.
    /// </summary>
    [Fact]
    public async Task CreateQuickMatch_ShouldFallbackToUserEmail_WhenDisplayNameClaimIsMissing()
    {
        // Arrange
        var fallbackUserId = $"auth0|fallback-{Guid.NewGuid():N}";
        const string fallbackEmail = "fallback@example.com";
        var sportId = Guid.NewGuid();
        sportId = await SeedSportDataAsync(sportId, $"FallbackPolo_{Guid.NewGuid():N}");

        var defaultClubId = Guid.Parse("11111111-1111-1111-1111-000000000001");
        var tempCityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using (var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                INSERT INTO public.countries (name, code) VALUES ('Ukraine', 'UA') ON CONFLICT DO NOTHING;
                INSERT INTO public.regions (countryid, name) SELECT id, 'Dnipro Region' FROM public.countries WHERE code = 'UA' ON CONFLICT DO NOTHING;
                INSERT INTO public.cities (id, regionid, name) SELECT @cityId, id, 'Dnipro' FROM public.regions WHERE name = 'Dnipro Region' ON CONFLICT DO NOTHING;",
                new { cityId = tempCityId });

            var actualCityId = await conn.QuerySingleAsync<Guid>(
                "SELECT id FROM public.cities WHERE name = 'Dnipro' LIMIT 1");

            await conn.ExecuteAsync(@"
                INSERT INTO public.clubs (id, cityid, name, createdat) 
                VALUES (@clubId, @cityId, 'TTA Training Club', NOW()) 
                ON CONFLICT DO NOTHING;",
                new { clubId = defaultClubId, cityId = actualCityId });

            await conn.ExecuteAsync(@"
                UPDATE public.sportconfigurations 
                SET rosterlimit = 5, lineuplimit = 3 
                WHERE sportid = @sportId;",
                new { sportId });
        }

        var request = new
        {
            SportId = sportId
        };

        try
        {
            TestAuthHandler.CustomUserId = fallbackUserId;
            TestAuthHandler.CustomEmail = fallbackEmail;
            // Omit the display name claim to cover the ternary fallback branch
            TestAuthHandler.CustomDisplayName = string.Empty;

            // Act
            var response = await Client.PostAsJsonAsync($"{BaseUrl}/quick", request);

            // Assert 1: HTTP Response
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var result = await response.Content.ReadFromJsonAsync<QuickMatchResponse>();
            result.Should().NotBeNull();
            result!.Id.Should().NotBeEmpty();

            // Assert 2: Database State Verification - Persisted DisplayName falls back to user email
            using var checkConn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
            await checkConn.OpenAsync();

            var createdUser = await checkConn.QueryFirstOrDefaultAsync<User>(
                "SELECT id, email, displayname FROM public.users WHERE id = @userId",
                new { userId = fallbackUserId });

            createdUser.Should().NotBeNull("JIT user record must be provisioned in public.users");
            createdUser!.DisplayName.Should().Be(fallbackEmail, "displayname should fallback to user email when display name claim is missing");
        }
        finally
        {
            TestAuthHandler.CustomUserId = null;
            TestAuthHandler.CustomEmail = null;
            TestAuthHandler.CustomDisplayName = null;
        }
    }

    #endregion

    #region User Tracked Matches Tests

    /// <summary>
    /// Verifies that <see cref="MatchesController.CatchMatch"/> returns HTTP 200 OK 
    /// when an authorized user catches a valid match and team context.
    /// </summary>
    [Fact]
    public async Task CatchMatch_ShouldReturnOk_WhenRequestIsValid()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC Catch");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC Catch");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-CATCH-01");

        // Act
        var response = await Client.PostAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.CatchMatch"/> returns HTTP 401 Unauthorized 
    /// when authentication is disabled.
    /// </summary>
    [Fact]
    public async Task CatchMatch_ShouldReturnUnauthorized_WhenAuthenticationIsDisabled()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        try
        {
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.PostAsync($"{BaseUrl}/{matchId}/teams/{teamId}/catch", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            TestAuthHandler.IsEnabled = true;
        }
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.UncatchMatch"/> returns HTTP 200 OK 
    /// when a tracking link exists and is successfully removed by the user.
    /// </summary>
    [Fact]
    public async Task UncatchMatch_ShouldReturnOk_WhenTrackingExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC Uncatch");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC Uncatch");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-UNCATCH-01");

        // Catch the match first
        var catchResponse = await Client.PostAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch", null);
        catchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.UncatchMatch"/> returns HTTP 404 NotFound 
    /// when trying to remove a tracking link that does not exist.
    /// </summary>
    [Fact]
    public async Task UncatchMatch_ShouldReturnNotFound_WhenTrackingDoesNotExist()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC NoCatch");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC NoCatch");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-NOCATCH-01");

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.AddUserToTrackedMatch"/> returns HTTP 200 OK 
    /// when the caller tracks the match and the target user exists.
    /// </summary>
    [Fact]
    public async Task AddUserToTrackedMatch_ShouldReturnOk_WhenRequestIsValidAndUserExists()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC Share");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC Share");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-SHARE-01");

        // 1. Caller catches the match first
        await Client.PostAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch", null);

        // 2. Seed target user
        var targetUserId = $"auth0|target-{Guid.NewGuid():N}";
        const string targetEmail = "target.user@test.com";
        await SeedUserAsync(targetUserId, targetEmail, "Target User");

        var request = new AddUserToTrackedMatchRequest(targetEmail);

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch/add-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.AddUserToTrackedMatch"/> returns HTTP 400 BadRequest 
    /// when the target email format is invalid.
    /// </summary>
    [Fact]
    public async Task AddUserToTrackedMatch_ShouldReturnBadRequest_WhenEmailIsInvalid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var request = new AddUserToTrackedMatchRequest("plainaddress");

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/teams/{teamId}/catch/add-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.AddUserToTrackedMatch"/> returns HTTP 409 Conflict 
    /// when the caller is not tracking the match before attempting to share it.
    /// </summary>
    [Fact]
    public async Task AddUserToTrackedMatch_ShouldReturnConflict_WhenCallerDoesNotTrackMatch()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC NoTrack");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC NoTrack");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-NOTRACK-01");

        const string targetEmail = "target.notrack@test.com";
        await SeedUserAsync($"auth0|target-{Guid.NewGuid():N}", targetEmail, "Target NoTrack");

        var request = new AddUserToTrackedMatchRequest(targetEmail);

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch/add-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies that <see cref="MatchesController.GetCatchedMatches"/> returns HTTP 200 OK 
    /// with the list of matches tracked by the authenticated user.
    /// </summary>
    [Fact]
    public async Task GetCatchedMatches_ShouldReturnOkWithMatches_WhenUserHasTrackedMatches()
    {
        // Arrange
        var context = await SetupTournamentContextAsync(TestUserId);
        var homeId = await SeedTeamAsync(context.CityId, context.SportId, "Home FC GetCatched");
        var guestId = await SeedTeamAsync(context.CityId, context.SportId, "Guest FC GetCatched");

        var matchId = Guid.NewGuid();
        await SeedMatchAsync(matchId, context.TournamentId, homeId, guestId, "M-GETCATCH-01");

        // Catch the match
        await Client.PostAsync($"{BaseUrl}/{matchId}/teams/{homeId}/catch", null);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/catch");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var catchedMatches = await response.Content.ReadFromJsonAsync<IEnumerable<MatchWithDetailsResponse>>();

        catchedMatches.Should().NotBeNull();
        var list = catchedMatches!.ToList();
        list.Should().Contain(m => m.Id == matchId);
    }

    #endregion

    #region Helpers for Seed Operations

    private async Task<(Guid MatchId, Guid TeamId, Guid LineupId)> SeedAnalyticsEnvironmentAsync(string ownerId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

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

        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'Analytics', 'Player', '2000-01-01', 0, NOW())", new { id = playerId, cid = clubId }, transaction: transaction);

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) VALUES (@id, @pid, @tid, @teamid, 7, @posid, NOW())", new { id = rosterId, pid = playerId, tid = tournamentId, teamid = teamId, posid = posId }, transaction: transaction);

        var lineupId = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) VALUES (@id, @mid, @rid, 7, @posid)", new { id = lineupId, mid = matchId, rid = rosterId, posid = posId }, transaction: transaction);

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

    private async Task<(Guid MatchId, Guid LineupId1, Guid LineupId2)> SetupPresenceMatchContextAsync(string tournamentOwnerId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

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

    private async Task SeedAccessPolicyAsync(string userId, int role, int targetType, Guid targetId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
              VALUES (@id, @userId, @role, @targetType, @targetId, @now)",
            new { id = Guid.NewGuid(), userId, role, targetType, targetId, now = DateTime.UtcNow });
    }

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
        await SeedUserAsync(ownerId, "owner@example.com", "Tournament Owner");
        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Sport-" + Guid.NewGuid());
        var configId = await SeedConfigurationAsync(sportId);
        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, ownerId, "Tourney-" + Guid.NewGuid());

        return (tournamentId, cityId, sportId);
    }

    private async Task SeedMatchAsync(Guid id, Guid tournamentId, Guid homeId, Guid guestId, string matchNumber, int? homeScore = null, int? guestScore = null)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, matchnumber, homescore, guestscore, createdat) 
            VALUES (@id, @tId, @hId, @gId, @date, @num, @homeScore, @guestScore, @created)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tId", tournamentId);
        cmd.Parameters.AddWithValue("hId", homeId);
        cmd.Parameters.AddWithValue("gId", guestId);
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow.AddHours(2));
        cmd.Parameters.AddWithValue("num", matchNumber);
        cmd.Parameters.AddWithValue("homeScore", (object?)homeScore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("guestScore", (object?)guestScore ?? DBNull.Value);
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

    private async Task SeedUserAsync(string userId, string email, string displayName)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, @name, @created) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("name", displayName);
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
        await using var tx = await conn.BeginTransactionAsync();

        var configId = Guid.NewGuid();
        var shortName = name.Length > 10 ? name[..10] : name;

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