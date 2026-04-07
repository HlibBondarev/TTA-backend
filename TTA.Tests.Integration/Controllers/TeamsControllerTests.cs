using Dapper;
using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.DataAccess.Enums;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <c>TeamsController</c>, focusing on member management and authorization.
/// These tests verify that the <c>TeamAdmin</c> policy correctly grants or denies access based on the <c>teamId</c>.
/// </summary>
[Collection("DatabaseCollection")]
public class TeamsControllerTests(DatabaseFixture fixture) : BaseApiTest(fixture)
{
    /// <summary>
    /// Verifies that authorized users can successfully retrieve a list of team members.
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
    /// Verifies that a user with <c>TeamAdmin</c> permissions can successfully add a new member with a valid request.
    /// This test uses an email address to identify the user, as required by the controller logic.
    /// </summary>
    [Fact]
    public async Task AddMember_ShouldReturnOk_WhenRequestIsValid()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var newUserAuthId = $"auth0|new-user-{Guid.NewGuid()}";
        var newUserEmail = "new-member@example.com";

        await SeedFullContextAsync(teamId, clubId);

        // Seed the user in the DB so the handler can find them by email
        await SeedUserAsync(newUserAuthId, "New Player", newUserEmail);

        // Controller expects Email in the request body
        var request = new AddTeamMemberRequest(newUserEmail, TeamRole.Player, true);

        // Act
        var response = await Client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that the system returns <c>400 Bad Request</c> when the <c>AddTeamMemberRequest</c> 
    /// contains invalid data, such as an incorrectly formatted email.
    /// </summary>
    [Fact]
    public async Task AddMember_ShouldReturnBadRequest_WhenRequestIsInvalid()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        // Providing an invalid email to trigger validation error
        var request = new AddTeamMemberRequest("not-an-email", TeamRole.Player, true);

        // Act
        var response = await Client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that a user with <c>TeamAdmin</c> permissions can successfully terminate an existing membership.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnNoContent_WhenMembershipExists()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var memberId = $"auth0|member-{Guid.NewGuid()}";

        await SeedFullContextAsync(teamId, clubId);
        await SeedUserAsync(memberId, "Ex Member", "ex@member.com");
        await SeedMembershipAsync(membershipId, teamId, memberId);

        // Act
        var response = await Client.DeleteAsync($"/api/teams/{teamId}/members/{membershipId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Verifies that the system returns <c>404 Not Found</c> when trying to terminate a membership that does not exist.
    /// </summary>
    [Fact]
    public async Task TerminateMember_ShouldReturnNotFound_WhenMembershipDoesNotExist()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await SeedFullContextAsync(teamId, clubId);

        // Act
        var response = await Client.DeleteAsync($"/api/teams/{teamId}/members/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #region Private Helpers

    /// <summary>
    /// Seeds a complete data hierarchy (Country -> Region -> City -> Club -> Team) 
    /// and grants <c>TeamAdmin</c> permissions to the <c>TestUserId</c>.
    /// </summary>
    /// <param name="teamId">The unique identifier for the team to be created.</param>
    /// <param name="clubId">The unique identifier for the club to be created.</param>
    private async Task SeedFullContextAsync(Guid teamId, Guid clubId)
    {
        // Synchronize database user with claims from TestAuthHandler
        await SeedUserAsync(TestUserId, "TestUser", "test@example.com");

        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var cityId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Static geography data
        await conn.ExecuteAsync("INSERT INTO public.countries (id, name, code) VALUES (380, 'Ukraine', 'UA') ON CONFLICT DO NOTHING");
        await conn.ExecuteAsync("INSERT INTO public.regions (id, name, countryid) VALUES (1, 'Kyiv', 380) ON CONFLICT DO NOTHING");
        await conn.ExecuteAsync("INSERT INTO public.cities (id, name, regionid) VALUES (@cityId, 'Kyiv', 1) ON CONFLICT DO NOTHING", new { cityId });
        await conn.ExecuteAsync("INSERT INTO public.sports (id, name) VALUES (@sportId, 'Football') ON CONFLICT DO NOTHING", new { sportId });

        // Core entities
        await conn.ExecuteAsync(@"
            INSERT INTO public.clubs (id, name, cityid, createdat) 
            VALUES (@clubId, 'Integration Test Club', @cityId, @now)", new { clubId, cityId, now });

        await conn.ExecuteAsync(@"
            INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) 
            VALUES (@teamId, @clubId, @sportId, 'Integration Test Team', 0, @now)", new { teamId, clubId, sportId, now });

        // Grant Team-level access (TargetType 2) to satisfy the TeamAdmin policy requirement
        await conn.ExecuteAsync(@"
            INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @userId, 0, 2, @teamId, @now)",
            new { id = Guid.NewGuid(), userId = TestUserId, teamId, now });
    }

    /// <summary>
    /// Inserts or updates a user record to ensure consistency with authentication claims.
    /// </summary>
    /// <param name="userId">The unique identifier (Auth0 ID) for the user.</param>
    /// <param name="displayName">The name to be displayed for the user.</param>
    /// <param name="email">The user's electronic mail address.</param>
    private async Task SeedUserAsync(string userId, string displayName, string email)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, displayname, email, createdat) 
            VALUES (@id, @displayName, @email, NOW()) 
            ON CONFLICT (id) DO UPDATE SET displayname = @displayName, email = @email",
            new { id = userId, displayName, email });
    }

    /// <summary>
    /// Creates a membership record for a specific team and user in the database.
    /// </summary>
    /// <param name="id">The unique identifier for the membership record.</param>
    /// <param name="teamId">The ID of the team the user is joining.</param>
    /// <param name="userId">The ID of the user joining the team.</param>
    private async Task SeedMembershipAsync(Guid id, Guid teamId, string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO public.teammemberships (id, userid, teamid, roleinteam, joinedat, isprimary) 
            VALUES (@id, @userId, @teamId, 2, NOW(), true)",
            new { id, userId, teamId });
    }

    #endregion
}