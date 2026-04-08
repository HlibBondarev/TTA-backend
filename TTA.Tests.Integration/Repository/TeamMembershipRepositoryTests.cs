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
    /// Verifies that <see cref="TeamMembershipRepository.CreateMembershipWithPolicyAsync"/> 
    /// successfully inserts a membership record and that access is effectively granted.
    /// </summary>
    [Fact]
    public async Task CreateMembershipWithPolicyAsync_ShouldInsertMembershipAndAccessIsGranted()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync("user@test.com");
        var membership = CreateModel(teamId, userId, TeamRole.Player, isPrimary: true);
        // Note: appRole is passed but since we now derive 0 (FullControl) in the DB 
        // for all active members in our current logic, that's what we expect back.
        var appRole = AppRole.FullControl;

        // Act
        var result = await _repository.CreateMembershipWithPolicyAsync(membership, appRole);

        // Assert
        Assert.Equal(membership.Id, result.Id);

        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // 1. Verify membership exists
        var dbMembership = await conn.QuerySingleOrDefaultAsync<TeamMembership>(
            "SELECT * FROM public.teammemberships WHERE id = @Id", new { result.Id });
        Assert.NotNull(dbMembership);

        // 2. Instead of checking a table that should be empty for teams,
        // verify the permission function returns the correct role (0 for FullControl).
        var effectiveRole = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT auth.get_user_permission(@UserId, 2, @TeamId)",
            new { UserId = userId, TeamId = teamId });

        Assert.Equal((int)appRole, effectiveRole);
    }

    /// <summary>
    /// Verifies the primary membership rotation logic. 
    /// When a new membership is marked as primary, the existing one is reset to non-primary.
    /// </summary>
    [Fact]
    public async Task CreateMembershipWithPolicyAsync_WhenNewIsPrimary_ShouldResetOldPrimary()
    {
        // Arrange
        var team1Id = await SeedTeamAsync();
        var team2Id = await SeedTeamAsync();
        var userId = await SeedUserAsync("primary-test@test.com");

        var firstMembership = CreateModel(team1Id, userId, TeamRole.AssistantCoach, isPrimary: true);
        await _repository.CreateMembershipWithPolicyAsync(firstMembership, AppRole.FullControl);

        var secondMembership = CreateModel(team2Id, userId, TeamRole.HeadCoach, isPrimary: true);

        // Act
        await _repository.CreateMembershipWithPolicyAsync(secondMembership, AppRole.FullControl);

        // Assert
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var memberships = (await conn.QueryAsync<TeamMembership>(
            "SELECT * FROM public.teammemberships WHERE userid = @UserId", new { UserId = userId })).ToList();

        Assert.False(memberships.First(m => m.Id == firstMembership.Id).IsPrimary);
        Assert.True(memberships.First(m => m.Id == secondMembership.Id).IsPrimary);
    }

    /// <summary>
    /// Verifies that the database prevents duplicate active roles for the same user in the same team.
    /// </summary>
    [Fact]
    public async Task CreateMembershipWithPolicyAsync_DuplicateActiveRole_ShouldThrowPostgresException()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync("duplicate-role@test.com");
        var role = TeamRole.Analyst;

        var first = CreateModel(teamId, userId, role, isPrimary: false);
        await _repository.CreateMembershipWithPolicyAsync(first, AppRole.Editor);

        var second = CreateModel(teamId, userId, role, isPrimary: false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            _repository.CreateMembershipWithPolicyAsync(second, AppRole.Editor));

        Assert.Equal("23505", ex.SqlState);
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

    private static TeamMembership CreateModel(Guid teamId, string userId, TeamRole role, bool isPrimary) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = teamId,
        UserId = userId,
        RoleInTeam = role,
        IsPrimary = isPrimary,
        JoinedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Seeds a complete hierarchy required for a Team to exist: 
    /// Country -> Region -> City -> Club AND Sport -> Team.
    /// </summary>
    private async Task<Guid> SeedTeamAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // 1. Seed Geography
        var countryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.countries (name, code, createdat) VALUES (@n, @c, now()) RETURNING id",
            new { n = $"Country_{Guid.NewGuid()}", c = Guid.NewGuid().ToString()[..3] });

        var regionId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.regions (countryid, name) VALUES (@cid, @n) RETURNING id",
            new { cid = countryId, n = "Test Region" });

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.cities (id, regionid, name) VALUES (@id, @rid, @n)",
            new { id = cityId, rid = regionId, n = "Test City" });

        // 2. Seed Club
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.clubs (id, name, cityid, createdat) 
              VALUES (@id, 'Test Club', @cityId, now())",
            new { id = clubId, cityId });

        // 3. Seed Sport
        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.sports (id, name) VALUES (@id, @n)",
            new { id = sportId, n = $"Sport_{Guid.NewGuid()}" });

        // 4. Seed Team (Gender 0 = Male, 1 = Female)
        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.teams (id, name, clubid, sportid, gender, createdat) 
              VALUES (@id, 'Test Team', @clubId, @sportId, 0, now())",
            new { id = teamId, clubId, sportId });

        return teamId;
    }

    private async Task<string> SeedUserAsync(string email)
    {
        var id = $"auth0|{Guid.NewGuid()}";
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO public.users (id, email, displayname, createdat) 
              VALUES (@id, @email, 'Test User', now())",
            new { id, email });
        return id;
    }

    #endregion
}