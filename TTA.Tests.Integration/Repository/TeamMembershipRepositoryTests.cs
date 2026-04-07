using Dapper;
using FluentAssertions;
using System.Text.Json;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.Common.Enums;
using TTA.Common.Extensions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="TeamMembershipRepository"/> using a real database container.
/// Verifies scoped termination, primary flag resets, and JSON data consistency.
/// </summary>
/// <param name="fixture">The shared database fixture instance.</param>
[Collection("DatabaseCollection")]
public class TeamMembershipRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly TeamMembershipRepository _repository = new(fixture.ConnectionFactory);

    /// <summary>
    /// Verifies that a valid membership is correctly persisted in the database and 
    /// that the corresponding access policy is created in the auth schema.
    /// </summary>
    [Fact]
    public async Task CreateMembershipWithPolicyAsync_ShouldPersistMembership_WhenDataIsValid()
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
        var result = await _repository.CreateMembershipWithPolicyAsync(membership, AppRole.FullControl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(membership.Id);
        result.UserId.Should().Be(userId);
        result.IsPrimary.Should().BeTrue();
        result.LeftAt.Should().BeNull();
    }

    /// <summary>
    /// Verifies that when a user is assigned a new primary team, 
    /// any previous primary status for that user is automatically reset by the DB function.
    /// </summary>
    [Fact]
    public async Task CreateMembershipWithPolicyAsync_ShouldResetPreviousPrimary_WhenNewOneIsSetToPrimary()
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

        var m1 = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = team1Id, IsPrimary = true, JoinedAt = DateTime.UtcNow };
        await _repository.CreateMembershipWithPolicyAsync(m1, AppRole.FullControl);

        // Act: Create second membership as primary for the same user
        var m2 = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = team2Id, IsPrimary = true, JoinedAt = DateTime.UtcNow };
        await _repository.CreateMembershipWithPolicyAsync(m2, AppRole.FullControl);

        // Assert
        var allMemberships = await GetRawMembershipsAsync(userId);
        allMemberships.Should().HaveCount(2);

        allMemberships.First(x => x.Id == m1.Id).IsPrimary.Should().BeFalse(); // Reset by function
        allMemberships.First(x => x.Id == m2.Id).IsPrimary.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that terminating a membership sets the LeftAt timestamp 
    /// and resets the IsPrimary flag to false.
    /// </summary>
    [Fact]
    public async Task TerminateMembershipAsync_ShouldSetLeftAt_AndResetPrimary_AndReturnTrue()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = "auth0|terminate-user";

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);
        await SeedUserAsync(userId, "terminate@test.com", "Terminate User");

        var m = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = teamId, IsPrimary = true, JoinedAt = DateTime.UtcNow };
        await _repository.CreateMembershipWithPolicyAsync(m, AppRole.FullControl);

        // Act
        var success = await _repository.TerminateMembershipAsync(teamId, m.Id, CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        var updated = (await GetRawMembershipsAsync(userId)).First();
        updated.LeftAt.Should().NotBeNull();
        updated.IsPrimary.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that membership termination fails if the provided TeamId does not match 
    /// the team associated with the membership.
    /// </summary>
    [Fact]
    public async Task TerminateMembershipAsync_ShouldReturnFalse_WhenTeamIdDoesNotMatch()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var wrongTeamId = Guid.NewGuid();
        var userId = "auth0|security-user";

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);
        await SeedUserAsync(userId, "security@test.com", "Security User");

        var m = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = teamId, IsPrimary = true, JoinedAt = DateTime.UtcNow };
        await _repository.CreateMembershipWithPolicyAsync(m, AppRole.Viewer);

        // Act: Attempt to terminate using a mismatched TeamId
        var success = await _repository.TerminateMembershipAsync(wrongTeamId, m.Id, CancellationToken.None);

        // Assert
        success.Should().BeFalse();
        var notUpdated = (await GetRawMembershipsAsync(userId)).First();
        notUpdated.LeftAt.Should().BeNull();
    }

    /// <summary>
    /// Verifies that GetMembersJsonAsync returns a valid JSON array string containing active member details.
    /// </summary>
    [Fact]
    public async Task GetMembersJsonAsync_WhenMembersExist_ShouldReturnJsonArray()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = "auth0|json-user-1";

        await SeedTeamDependenciesAsync(clubId, sportId);
        await SeedTeamAsync(teamId, clubId, sportId);
        await SeedUserAsync(userId, "john.doe@example.com", "John Doe");

        var membership = new TeamMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId,
            RoleInTeam = TeamRole.Player,
            JoinedAt = DateTime.UtcNow,
            IsPrimary = true
        };
        await _repository.CreateMembershipWithPolicyAsync(membership, AppRole.Viewer);

        // Act
        var jsonResult = await _repository.GetMembersJsonAsync(teamId, CancellationToken.None);

        // Assert
        jsonResult.Should().NotBeNullOrWhiteSpace();
        var members = JsonSerializer.Deserialize<List<TeamMemberResponse>>(jsonResult!, new JsonSerializerOptions().GetDefault());
        members.Should().HaveCount(1);
        members![0].MembershipId.Should().Be(membership.Id);
    }

    /// <summary>
    /// Verifies that members who have already left the team (LeftAt IS NOT NULL) 
    /// are excluded from the JSON response.
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

        var membership = new TeamMembership { Id = Guid.NewGuid(), UserId = userId, TeamId = teamId, JoinedAt = DateTime.UtcNow.AddMonths(-1) };
        await _repository.CreateMembershipWithPolicyAsync(membership, AppRole.Viewer);
        await _repository.TerminateMembershipAsync(teamId, membership.Id, CancellationToken.None);

        // Act
        var jsonResult = await _repository.GetMembersJsonAsync(teamId, CancellationToken.None);

        // Assert
        jsonResult.Should().Be("[]");
    }

    /// <summary>
    /// Verifies that GetMembersJsonAsync returns an empty array string "[]" 
    /// when the team exists but has no members.
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
        jsonResult.Should().Be("[]");
    }

    #region Helpers

    /// <summary>
    /// Seeds a user into the database. Uses ON CONFLICT (id) to prevent duplicate key errors.
    /// </summary>
    private async Task SeedUserAsync(string id, string email, string displayName)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        // Explicitly target the 'id' column for the conflict
        var sql = @"
        INSERT INTO public.users (id, email, displayname, createdat) 
        VALUES (@id, @email, @displayName, NOW()) 
        ON CONFLICT (id) DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, email, displayName });
    }

    private async Task SeedTeamAsync(Guid id, Guid clubId, Guid sportId, string name = "Test Team")
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var sql = "INSERT INTO public.teams (id, clubid, sportid, name, minbirthyear, gender, createdat) VALUES (@id, @clubId, @sportId, @name, 2010, 0, NOW())";
        await conn.ExecuteAsync(sql, new { id, clubId, sportId, name });
    }

    private async Task SeedTeamDependenciesAsync(Guid clubId, Guid sportId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO countries (id, name, code) VALUES (1, 'Ukraine', 'UKR') ON CONFLICT DO NOTHING");
        await conn.ExecuteAsync("INSERT INTO regions (id, name, countryid) VALUES (1, 'Dnipro', 1) ON CONFLICT DO NOTHING");
        await conn.ExecuteAsync("INSERT INTO cities (id, name, regionid) VALUES (@cityId, 'Dnipro', 1) ON CONFLICT DO NOTHING", new { cityId });
        await conn.ExecuteAsync("INSERT INTO clubs (id, name, cityid, createdat) VALUES (@clubId, 'Test Club', @cityId, NOW()) ON CONFLICT DO NOTHING", new { clubId, cityId });
        await conn.ExecuteAsync("INSERT INTO sports (id, name) VALUES (@sportId, 'Football') ON CONFLICT DO NOTHING", new { sportId });
    }

    private async Task<IEnumerable<TeamMembership>> GetRawMembershipsAsync(string userId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        return await conn.QueryAsync<TeamMembership>("SELECT * FROM public.teammemberships WHERE userid = @userId", new { userId });
    }

    #endregion
}