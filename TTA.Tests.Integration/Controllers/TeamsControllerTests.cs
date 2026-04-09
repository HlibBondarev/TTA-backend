using Dapper;
using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.Common.Enums;
using TTA.DataAccess.Enums;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="TeamsController"/>.
/// Verifies team membership management, including retrieval, addition, and secure termination.
/// </summary>
[Collection("DatabaseCollection")]
public class TeamsControllerTests : BaseApiTest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TeamsControllerTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture instance.</param>
    public TeamsControllerTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    #region Members Management

    /// <summary>
    /// Verifies that authorized users can successfully retrieve a list of members for an existing team.
    /// </summary>
    [Fact]
    public async Task GetMembers_ShouldReturnOk_WhenTeamExists()
    {
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        var response = await Client.GetAsync($"/api/teams/{teamId}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a user with sufficient permissions can add a new member to the team.
    /// </summary>
    [Fact]
    public async Task AddMember_ShouldReturnOk_WhenRequestIsValid()
    {
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var newUserEmail = "new-member@example.com";

        await SeedFullContextAsync(teamId, clubId);
        await SeedUserAsync($"auth0|{Guid.NewGuid()}", "New Member", newUserEmail);

        var request = new AddTeamMemberRequest(newUserEmail, TeamRole.Player, false);

        var response = await Client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that the system returns 400 Bad Request when providing invalid membership data.
    /// </summary>
    [Fact]
    public async Task AddMember_ShouldReturnBadRequest_WhenRequestIsInvalid()
    {
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        var request = new AddTeamMemberRequest("invalid-email", TeamRole.Player, false);

        var response = await Client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Terminate Membership

    /// <summary>
    /// Verifies that an authorized user can successfully terminate a team membership.
    /// Returns 204 NoContent upon successful completion of the transaction.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnNoContent_WhenMembershipExists()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        var victimEmail = "player_to_terminate@example.com";
        var victimUserId = $"auth0|{Guid.NewGuid()}";

        await SeedFullContextAsync(teamId, clubId);
        await SeedUserAsync(victimUserId, "Victim User", victimEmail);

        await SeedMembershipAsync(Guid.NewGuid(), teamId, victimUserId, (int)TeamRole.Player);
        await SeedAccessPolicyAsync(victimUserId, (int)TargetScope.Team, teamId, (int)AppRole.Viewer);

        var request = new TerminateMembershipRequest(victimEmail, TeamRole.Player, DateTime.UtcNow);

        // Act
        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}/members/terminate")
        {
            Content = JsonContent.Create(request)
        };
        var response = await Client.SendAsync(httpRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Verifies that the endpoint returns 404 Not Found when attempting to terminate a non-existent membership.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnNotFound_WhenMembershipDoesNotExist()
    {
        var teamId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        var request = new TerminateMembershipRequest("nonexistent@example.com", TeamRole.HeadCoach, null);

        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}/members/terminate")
        {
            Content = JsonContent.Create(request)
        };
        var response = await Client.SendAsync(httpRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that a user without appropriate access policies receives 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnForbidden_WhenUserHasNoPolicy()
    {
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var victimEmail = "victim@example.com";
        var victimUserId = $"auth0|{Guid.NewGuid()}";

        using (var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.OpenAsync();
            await SeedRequiredLocationDataInternalAsync(conn, clubId);
            await SeedTeamInternalAsync(conn, teamId, clubId);
            await SeedUserInternalAsync(conn, TestUserId, "No Access User", "test@example.com");
            await SeedUserInternalAsync(conn, victimUserId, "Victim", victimEmail);
        }

        await SeedMembershipAsync(Guid.NewGuid(), teamId, victimUserId, (int)TeamRole.Player);

        var request = new TerminateMembershipRequest(victimEmail, TeamRole.Player, null);

        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}/members/terminate")
        {
            Content = JsonContent.Create(request)
        };
        var response = await Client.SendAsync(httpRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Seeding Helpers

    private async Task SeedFullContextAsync(Guid teamId, Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        await SeedRequiredLocationDataInternalAsync(conn, clubId);
        await SeedTeamInternalAsync(conn, teamId, clubId);
        await SeedUserInternalAsync(conn, TestUserId, "Admin", "test@example.com");
        await SeedAccessPolicyInternalAsync(conn, TestUserId, (int)TargetScope.Club, clubId, (int)AppRole.FullControl);
    }

    private async Task SeedRequiredLocationDataInternalAsync(NpgsqlConnection conn, Guid clubId)
    {
        await conn.ExecuteAsync("INSERT INTO public.countries (id, name, code) VALUES (1, 'Ukraine', 'UA') ON CONFLICT DO NOTHING");
        await conn.ExecuteAsync("INSERT INTO public.regions (id, name, countryid) VALUES (1, 'Test Region', 1) ON CONFLICT DO NOTHING");

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.cities (id, name, regionid) VALUES (@cityId, 'Test City', 1) ON CONFLICT DO NOTHING", new { cityId });

        await conn.ExecuteAsync(@"
            INSERT INTO public.clubs (id, name, cityid, createdat) 
            VALUES (@clubId, 'Test Club', @cityId, @now) ON CONFLICT DO NOTHING",
            new { clubId, cityId, now = DateTime.UtcNow });
    }

    private async Task SeedTeamInternalAsync(NpgsqlConnection conn, Guid id, Guid clubId)
    {
        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name) VALUES (@sportId, 'Football') 
            ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name", new { sportId });

        await conn.ExecuteAsync(@"
            INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) 
            VALUES (@id, @clubId, @sportId, 'First Team', 0, @now)",
            new { id, clubId, sportId, now = DateTime.UtcNow });
    }

    private async Task SeedUserInternalAsync(NpgsqlConnection conn, string userId, string name, string email)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, displayname, email, createdat) 
            VALUES (@userId, @name, @email, @now) ON CONFLICT (id) DO NOTHING",
            new { userId, name, email, now = DateTime.UtcNow });
    }

    private async Task SeedAccessPolicyInternalAsync(NpgsqlConnection conn, string userId, int scope, Guid targetId, int role)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @userId, @role, @scope, @targetId, @now)",
            new { id = Guid.NewGuid(), userId, role, scope, targetId, now = DateTime.UtcNow });
    }

    private async Task SeedUserAsync(string userId, string name, string email)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await SeedUserInternalAsync(conn, userId, name, email);
    }

    private async Task SeedAccessPolicyAsync(string userId, int scope, Guid targetId, int role)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await SeedAccessPolicyInternalAsync(conn, userId, scope, targetId, role);
    }

    private async Task SeedMembershipAsync(Guid id, Guid teamId, string userId, int role)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO public.teammemberships (id, userid, teamid, roleinteam, joinedat, isprimary) 
            VALUES (@id, @userId, @teamId, @role, @now, true)",
            new { id, userId, teamId, role, now = DateTime.UtcNow });
    }

    #endregion
}