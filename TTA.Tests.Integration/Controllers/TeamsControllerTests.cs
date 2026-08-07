using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.Common.Enums;
using TTA.DataAccess.Enums;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="TeamsController"/>.
/// Verifies team membership management, including retrieval, addition, and secure termination.
/// </summary>
[Collection("DatabaseCollection")]
public class TeamsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    #region Members Management

    /// <summary>
    /// Verifies that authorized users can successfully retrieve a list of members for an existing team.
    /// </summary>
    [Fact]
    public async Task GetMembers_ShouldReturnOk_WhenTeamExists()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        // Act
        var response = await Client.GetAsync($"/api/teams/{teamId}/members");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a user with sufficient permissions can add a new member to the team.
    /// Also checks if the corresponding AccessPolicy is created in the database.
    /// </summary>
    [Fact]
    public async Task AddMember_ShouldReturnOk_WhenRequestIsValid()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var newUserEmail = "new-member@example.com";
        var newUserAuthId = $"auth0|{Guid.NewGuid()}";

        await SeedFullContextAsync(teamId, clubId);
        await SeedUserAsync(newUserAuthId, "New Member", newUserEmail);

        var request = new AddTeamMemberRequest(newUserEmail, TeamRole.Player, false);

        // Act
        var response = await Client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify side effect: Ensure AccessPolicy was created for the new member
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // Fix for CS8600: Using 'dynamic?' to explicitly allow null from Dapper
        dynamic? policy = await conn.QueryFirstOrDefaultAsync(
            "SELECT role FROM auth.accesspolicies WHERE userid = @userId AND targetid = @teamId",
            new { userId = newUserAuthId, teamId });

        // Cast to object? for the FluentAssertion check
        ((object?)policy).Should().NotBeNull("an AccessPolicy record must be created automatically by the repository/database");
    }

    /// <summary>
    /// Verifies that the system returns 400 Bad Request when providing invalid membership data.
    /// </summary>
    [Fact]
    public async Task AddMember_ShouldReturnBadRequest_WhenRequestIsInvalid()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        var request = new AddTeamMemberRequest("invalid-email", TeamRole.Player, false);

        // Act
        var response = await Client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Terminate Membership

    /// <summary>
    /// Verifies that an authorized user can successfully terminate a team membership.
    /// Ensures that the associated AccessPolicy is also marked as expired.
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

        var request = new TerminateMembershipRequest(victimEmail, TeamRole.Player, DateTime.UtcNow.AddHours(1));

        // Act
        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}/members/terminate")
        {
            Content = JsonContent.Create(request)
        };
        var response = await Client.SendAsync(httpRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify side effect: AccessPolicy must have an expiration date set
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var expiresAt = await conn.QueryFirstOrDefaultAsync<DateTime?>(
            "SELECT expiresat FROM auth.accesspolicies WHERE userid = @victimUserId AND targetid = @teamId",
            new { victimUserId, teamId });

        expiresAt.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the endpoint returns 404 Not Found when attempting to terminate a non-existent membership.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnNotFound_WhenMembershipDoesNotExist()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        var request = new TerminateMembershipRequest("nonexistent@example.com", TeamRole.HeadCoach, null);

        // Act
        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}/members/terminate")
        {
            Content = JsonContent.Create(request)
        };
        var response = await Client.SendAsync(httpRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that a user without appropriate access policies receives 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnForbidden_WhenUserHasNoPolicy()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var victimEmail = "victim@example.com";
        var victimUserId = $"auth0|{Guid.NewGuid()}";

        using (var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.OpenAsync();
            await SeedRequiredLocationDataInternalAsync(conn);
            var cityId = await GetFirstCityIdAsync(conn);
            await SeedClubInternalAsync(conn, clubId, cityId);
            await SeedTeamInternalAsync(conn, teamId, clubId);
            await SeedUserInternalAsync(conn, TestUserId, "No Access User", "test@example.com");
            await SeedUserInternalAsync(conn, victimUserId, "Victim", victimEmail);
        }

        await SeedMembershipAsync(Guid.NewGuid(), teamId, victimUserId, (int)TeamRole.Player);

        var request = new TerminateMembershipRequest(victimEmail, TeamRole.Player, null);

        // Act
        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}/members/terminate")
        {
            Content = JsonContent.Create(request)
        };
        var response = await Client.SendAsync(httpRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that the endpoint returns <see cref="ValidationProblemDetails"/> (RFC 7807)
    /// when the request body contains invalid data, provided the user has sufficient permissions.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnValidationProblemDetails_WhenRequestIsInvalid()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        // We MUST seed the team and access policy first. 
        // Otherwise, the Authorization Middleware returns 403 Forbidden before reaching validation logic.
        await SeedFullContextAsync(teamId, clubId);

        var invalidRequest = new TerminateMembershipRequest("", TeamRole.Player, null);

        // Act
        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}/members/terminate")
        {
            Content = JsonContent.Create(invalidRequest)
        };
        var response = await Client.SendAsync(httpRequest);

        // Assert
        // Now that the user is authorized for the team, we expect a 400 Bad Request from validation[cite: 3, 5].
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        details.Should().NotBeNull();

        // Ensure the error key matches the property name in the request DTO[cite: 3]
        details!.Errors.Should().ContainKey("UserEmail");
    }

    #endregion

    #region Team

    /// <summary>
    /// Verifies that any user (including unauthenticated ones) can retrieve details 
    /// for an existing team by its unique identifier.
    /// </summary>
    [Fact]
    public async Task GetTeamById_ShouldReturnOk_WhenTeamExists()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        var previousAuthState = TestAuthHandler.IsEnabled;
        try
        {
            // Temporarily disable test authentication handler to simulate an anonymous request
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.GetAsync($"/api/teams/{teamId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var teamResponse = await response.Content.ReadFromJsonAsync<TeamResponse>();
            teamResponse.Should().NotBeNull();
            teamResponse!.Id.Should().Be(teamId);
            teamResponse.ClubId.Should().Be(clubId);
            teamResponse.Name.Should().Be("First Team");
            teamResponse.Gender.Should().Be(0); // Male
        }
        finally
        {
            // Restore initial authentication state
            TestAuthHandler.IsEnabled = previousAuthState;
        }
    }

    /// <summary>
    /// Verifies that the endpoint returns 404 Not Found when attempting to retrieve a team that does not exist.
    /// </summary>
    [Fact]
    public async Task GetTeamById_ShouldReturnNotFound_WhenTeamDoesNotExist()
    {
        // Arrange
        var nonExistentTeamId = Guid.NewGuid();

        // Act
        var response = await Client.GetAsync($"/api/teams/{nonExistentTeamId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Seeding Helpers

    /// <summary>
    /// Seeds a full testing context including location, club, team, user, and access policy records.
    /// </summary>
    /// <param name="teamId">The unique identifier for the team to seed.</param>
    /// <param name="clubId">The unique identifier for the club to seed.</param>
    private async Task SeedFullContextAsync(Guid teamId, Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        await SeedRequiredLocationDataInternalAsync(conn);
        var cityId = await GetFirstCityIdAsync(conn);
        await SeedClubInternalAsync(conn, clubId, cityId);
        await SeedTeamInternalAsync(conn, teamId, clubId);
        await SeedUserInternalAsync(conn, TestUserId, "Admin", "test@example.com");
        await SeedAccessPolicyInternalAsync(conn, TestUserId, (int)TargetScope.Club, clubId, (int)AppRole.FullControl);
    }

    /// <summary>
    /// Seeds initial location hierarchy (Country, Region, City) if it does not already exist.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    private static async Task SeedRequiredLocationDataInternalAsync(NpgsqlConnection conn)
    {
        // 1. Seed Country - Handle both unique name and code
        await conn.ExecuteAsync(@"
        INSERT INTO public.countries (name, code) 
        VALUES ('Ukraine', 'UA') 
        ON CONFLICT (name) DO NOTHING");

        var countryId = await conn.QueryFirstAsync<int>(
            "SELECT id FROM public.countries WHERE name = 'Ukraine'");

        // 2. Seed Region - UNIQUE(countryid, name)
        await conn.ExecuteAsync(@"
        INSERT INTO public.regions (countryid, name) 
        VALUES (@countryId, 'Test Region') 
        ON CONFLICT (countryid, name) DO NOTHING",
            new { countryId });

        var regionId = await conn.QueryFirstAsync<int>(
            "SELECT id FROM public.regions WHERE name = 'Test Region' AND countryid = @countryId",
            new { countryId });

        // 3. Seed City - UNIQUE(regionid, name)
        await conn.ExecuteAsync(@"
        INSERT INTO public.cities (id, name, regionid) 
        VALUES (@id, 'Test City', @regionId) 
        ON CONFLICT (regionid, name) DO NOTHING",
            new { id = Guid.NewGuid(), regionId });
    }

    /// <summary>
    /// Gets the identifier of the first available city in the database.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    /// <returns>The unique identifier of the city.</returns>
    private static async Task<Guid> GetFirstCityIdAsync(NpgsqlConnection conn)
        => await conn.QueryFirstAsync<Guid>("SELECT id FROM public.cities LIMIT 1");

    /// <summary>
    /// Seeds a club record into the database.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    /// <param name="clubId">The unique identifier of the club.</param>
    /// <param name="cityId">The unique identifier of the associated city.</param>
    private static async Task SeedClubInternalAsync(NpgsqlConnection conn, Guid clubId, Guid cityId)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO public.clubs (id, name, cityid, createdat) 
            VALUES (@clubId, 'Test Club', @cityId, @now) ON CONFLICT DO NOTHING",
            new { clubId, cityId, now = DateTime.UtcNow });
    }

    /// <summary>
    /// Seeds a team along with its sport and sport configuration dependencies.
    /// Uses a transaction and ON CONFLICT handling to safely resolve the 'Football' sport entity across parallel test runs.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    /// <param name="id">The unique identifier of the team.</param>
    /// <param name="clubId">The unique identifier of the owning club.</param>
    private static async Task SeedTeamInternalAsync(NpgsqlConnection conn, Guid id, Guid clubId)
    {
        using var tx = await conn.BeginTransactionAsync();

        var sportId = Guid.NewGuid();
        var defaultConfigId = Guid.NewGuid();

        // Atomically insert or fetch existing 'Football' sport entity
        var resolvedSportId = await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@sportId, 'Football', 'FB', @defaultConfigId) 
            ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
            RETURNING id",
            new { sportId, defaultConfigId }, tx);

        // If a new sport row was created, seed its corresponding sport configuration
        if (resolvedSportId == sportId)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO public.sportconfigurations (
                    id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit
                ) VALUES (
                    @defaultConfigId, @sportId, false, 2, 45, 'Standard', 18, 11, 7)
                ON CONFLICT DO NOTHING",
                new { defaultConfigId, sportId }, tx);
        }

        await tx.CommitAsync();

        await conn.ExecuteAsync(@"
            INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) 
            VALUES (@id, @clubId, @sportId, 'First Team', 0, @now)",
            new { id, clubId, sportId = resolvedSportId, now = DateTime.UtcNow });
    }

    /// <summary>
    /// Seeds a user record into the database.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="name">The display name of the user.</param>
    /// <param name="email">The email address of the user.</param>
    private static async Task SeedUserInternalAsync(NpgsqlConnection conn, string userId, string name, string email)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, displayname, email, createdat) 
            VALUES (@userId, @name, @email, @now) ON CONFLICT (id) DO NOTHING",
            new { userId, name, email, now = DateTime.UtcNow });
    }

    /// <summary>
    /// Seeds an access policy record for authorization checks.
    /// </summary>
    /// <param name="conn">The active database connection.</param>
    /// <param name="userId">The target user ID.</param>
    /// <param name="scope">The scope level (e.g., Club, Team).</param>
    /// <param name="targetId">The resource ID for the scope.</param>
    /// <param name="role">The granted application role.</param>
    private static async Task SeedAccessPolicyInternalAsync(NpgsqlConnection conn, string userId, int scope, Guid targetId, int role)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @userId, @role, @scope, @targetId, @now)",
            new { id = Guid.NewGuid(), userId, role, scope, targetId, now = DateTime.UtcNow });
    }

    /// <summary>
    /// Helper wrapper to seed a user record using a new connection.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="name">The display name of the user.</param>
    /// <param name="email">The email address of the user.</param>
    private async Task SeedUserAsync(string userId, string name, string email)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await SeedUserInternalAsync(conn, userId, name, email);
    }

    /// <summary>
    /// Helper wrapper to seed an access policy using a new connection.
    /// </summary>
    /// <param name="userId">The target user ID.</param>
    /// <param name="scope">The scope level.</param>
    /// <param name="targetId">The resource ID for the scope.</param>
    /// <param name="role">The granted application role.</param>
    private async Task SeedAccessPolicyAsync(string userId, int scope, Guid targetId, int role)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await SeedAccessPolicyInternalAsync(conn, userId, scope, targetId, role);
    }

    /// <summary>
    /// Seeds a team membership record into the database.
    /// </summary>
    /// <param name="id">The unique identifier of the membership.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="role">The role of the user within the team.</param>
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