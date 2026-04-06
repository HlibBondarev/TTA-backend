using Dapper;
using FluentAssertions;
using Npgsql;
using System.Text.Json;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.Common.Extensions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="TeamMembershipRepository"/> using a real database container.
/// </summary>
[Collection("DatabaseCollection")]
public class TeamMembershipRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly TeamMembershipRepository _repository = new(fixture.ConnectionFactory);

    [Fact]
    public async Task CreateMembershipAsync_ShouldPersistMembership_WhenDataIsValid()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = "auth0|test-user-123";

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);
        await SeedUserAsync(userId, "test@example.com", "Test User");

        var membership = new TeamMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId,
            RoleInTeam = TeamRole.HeadCoach,
            JoinedAt = DateTime.UtcNow,
            IsPrimary = true
        };

        // Act
        var result = await _repository.CreateMembershipAsync(membership, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(membership.Id);
        result.UserId.Should().Be(userId);
        result.IsPrimary.Should().BeTrue();
        result.LeftAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateMembershipAsync_ShouldResetPreviousPrimary_WhenNewOneIsSetToPrimary()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var userId = "auth0|primary-test-user";

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedUserAsync(userId, "primary@test.com", "Primary Tester");

        var team1Id = Guid.NewGuid();
        var team2Id = Guid.NewGuid();
        await SeedTeamAsync(team1Id, clubId, sportId, "Team 1");
        await SeedTeamAsync(team2Id, clubId, sportId, "Team 2");

        // First membership as primary
        var m1 = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = team1Id, IsPrimary = true };
        await _repository.CreateMembershipAsync(m1);

        // Act: Create second membership as primary for the same user
        var m2 = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = team2Id, IsPrimary = true };
        await _repository.CreateMembershipAsync(m2);

        // Assert
        var allMemberships = await GetRawMembershipsAsync(userId);
        allMemberships.Should().HaveCount(2);

        var first = allMemberships.First(x => x.Id == m1.Id);
        var second = allMemberships.First(x => x.Id == m2.Id);

        first.IsPrimary.Should().BeFalse(); // Should be reset by the function
        second.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task TerminateMembershipAsync_ShouldSetLeftAt_AndReturnTrue()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = "auth0|terminate-user";

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);
        await SeedUserAsync(userId, "terminate@test.com", "Terminate User");

        var m = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = teamId, IsPrimary = true };
        await _repository.CreateMembershipAsync(m);

        // Act
        var success = await _repository.TerminateMembershipAsync(m.Id);

        // Assert
        success.Should().BeTrue();

        var updated = (await GetRawMembershipsAsync(userId)).First();
        updated.LeftAt.Should().NotBeNull();
        updated.IsPrimary.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that GetMembersJsonAsync returns a valid JSON array containing member details
    /// when the team has active memberships.
    /// </summary>
    [Fact]
    public async Task GetMembersJsonAsync_WhenMembersExist_ShouldReturnJsonArray()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = "auth0|test-user-json-1";
        var userName = "John Doe";
        var userEmail = "john.doe@example.com";

        // 1. Seed all database requirements
        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);
        await SeedUserAsync(userId, userEmail, userName);

        var membership = new TeamMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId,
            RoleInTeam = TeamRole.Player,
            JoinedAt = DateTime.UtcNow,
            IsPrimary = true
        };
        await _repository.CreateMembershipAsync(membership, CancellationToken.None);

        // Act
        var jsonResult = await _repository.GetMembersJsonAsync(teamId, CancellationToken.None);

        // Assert
        jsonResult.Should().NotBeNullOrWhiteSpace();

        // Verify the structure by deserializing (using the same logic as the Handler)
        var options = new JsonSerializerOptions().GetDefault();
        var members = JsonSerializer.Deserialize<List<TeamMemberResponse>>(jsonResult!, options);

        members.Should().NotBeNull();
        members.Should().HaveCount(1);

        var member = members![0];
        member.MembershipId.Should().Be(membership.Id);
        member.UserId.Should().Be(userId);
        member.DisplayName.Should().Be(userName);
        member.Email.Should().Be(userEmail);
        member.RoleInTeam.Should().Be((int)TeamRole.Player);
    }

    /// <summary>
    /// Verifies that GetMembersJsonAsync returns null or an empty JSON representation
    /// when the team exists but has no active members.
    /// </summary>
    [Fact]
    public async Task GetMembersJsonAsync_WhenNoMembersExist_ShouldReturnEmptyResult()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);

        // Act
        var jsonResult = await _repository.GetMembersJsonAsync(teamId, CancellationToken.None);

        // Assert
        // Depending on SQL function implementation, it might return null or "[]"
        if (jsonResult != null)
        {
            jsonResult.Should().Be("[]");
        }
    }

    /// <summary>
    /// Verifies that GetMembersJsonAsync does not include members who have already left the team.
    /// </summary>
    [Fact]
    public async Task GetMembersJsonAsync_ShouldExcludeTerminatedMemberships()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = "auth0|left-user";

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);
        await SeedUserAsync(userId, "left@example.com", "Left User");

        var membership = new TeamMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId,
            RoleInTeam = TeamRole.Player,
            JoinedAt = DateTime.UtcNow.AddMonths(-1),
            IsPrimary = false
        };

        await _repository.CreateMembershipAsync(membership, CancellationToken.None);

        // Terminate the membership (sets LeftAt)
        await _repository.TerminateMembershipAsync(membership.Id, CancellationToken.None);

        // Act
        var jsonResult = await _repository.GetMembersJsonAsync(teamId, CancellationToken.None);

        // Assert
        var options = new JsonSerializerOptions().GetDefault();
        var members = jsonResult == null ? [] : JsonSerializer.Deserialize<List<TeamMemberResponse>>(jsonResult, options);

        members.Should().BeEmpty();
    }

    #region Helpers

    private async Task SeedUserAsync(string id, string email, string displayName)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        var sql = "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, @displayName, NOW()) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, email, displayName });
    }

    private async Task SeedTeamAsync(Guid id, Guid clubId, Guid sportId, string name = "Test Team")
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        var sql = "INSERT INTO public.teams (id, clubid, sportid, name, minbirthyear, gender, createdat) VALUES (@id, @clubId, @sportId, @name, 2010, 0, NOW())";
        await conn.ExecuteAsync(sql, new { id, clubId, sportId, name });
    }

    private async Task SeedTeamDependenciesAsync(Guid clubId, Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        var cityId = Guid.NewGuid();

        await conn.ExecuteAsync("INSERT INTO countries (id, name, code) VALUES (1, 'Ukraine', 'UKR') ON CONFLICT DO NOTHING", null, tx);
        await conn.ExecuteAsync("INSERT INTO regions (id, name, countryid) VALUES (1, 'Dnipro', 1) ON CONFLICT DO NOTHING", null, tx);
        await conn.ExecuteAsync("INSERT INTO cities (id, name, regionid) VALUES (@cityId, 'Dnipro', 1) ON CONFLICT DO NOTHING", new { cityId }, tx);
        await conn.ExecuteAsync("INSERT INTO clubs (id, name, cityid, createdat) VALUES (@clubId, 'Test Club', @cityId, NOW()) ON CONFLICT DO NOTHING", new { clubId, cityId }, tx);
        await conn.ExecuteAsync("INSERT INTO sports (id, name) VALUES (@sportId, 'Football') ON CONFLICT DO NOTHING", new { sportId }, tx);

        await tx.CommitAsync();
    }

    private async Task<IEnumerable<TeamMembership>> GetRawMembershipsAsync(string userId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        return await conn.QueryAsync<TeamMembership>(
            "SELECT * FROM public.teammemberships WHERE userid = @userId", new { userId });
    }

    #endregion
}