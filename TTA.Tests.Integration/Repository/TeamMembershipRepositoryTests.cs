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
/// Verifies membership lifecycle, primary team rotation logic, and JSON reporting consistency.
/// </summary>
/// <param name="fixture">The shared database fixture instance.</param>
[Collection("DatabaseCollection")]
public class TeamMembershipRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly TeamMembershipRepository _repository = new(fixture.ConnectionFactory);

    #region Create & Update Tests

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.CreateMembershipWithPolicyAsync"/> correctly persists 
    /// a membership record and ensures the effective role is granted via the database function.
    /// </summary>
    [Fact]
    public async Task CreateMembershipWithPolicyAsync_ShouldPersistMembership_AndGrantAccess()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync($"user_{Guid.NewGuid()}@test.com");
        var membership = CreateModel(teamId, userId, TeamRole.HeadCoach, isPrimary: true);
        var expectedAppRole = AppRole.FullControl;

        // Act
        var result = await _repository.CreateMembershipWithPolicyAsync(membership, expectedAppRole);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(membership.Id);

        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var dbMembership = await conn.QuerySingleOrDefaultAsync<TeamMembership>(
            "SELECT * FROM public.teammemberships WHERE id = @Id", new { result.Id });
        dbMembership.Should().NotBeNull();

        var effectiveRole = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT auth.get_user_permission(@UserId, 2, @TeamId)",
            new { UserId = userId, TeamId = teamId });

        effectiveRole.Should().Be((int)expectedAppRole);
    }

    /// <summary>
    /// Verifies the primary membership rotation logic. 
    /// When a user is assigned a new primary team, any previous primary flag for that user must be reset.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateMembershipWithPolicyAsync_ShouldHandlePrimaryRotationCorrectly(bool newIsPrimary)
    {
        // Arrange
        var team1Id = await SeedTeamAsync();
        var team2Id = await SeedTeamAsync();
        var userId = await SeedUserAsync($"primary_{Guid.NewGuid()}@test.com");

        var oldMembership = CreateModel(team1Id, userId, TeamRole.Player, isPrimary: true);
        await _repository.CreateMembershipWithPolicyAsync(oldMembership, AppRole.Viewer);

        // Act
        var newMembership = CreateModel(team2Id, userId, TeamRole.AssistantCoach, isPrimary: newIsPrimary);
        await _repository.CreateMembershipWithPolicyAsync(newMembership, AppRole.FullControl);

        // Assert
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var oldIsPrimary = await conn.ExecuteScalarAsync<bool>(
            "SELECT isprimary FROM public.teammemberships WHERE id = @Id", new { oldMembership.Id });

        if (newIsPrimary)
            oldIsPrimary.Should().BeFalse("because the new primary membership should have triggered a reset");
        else
            oldIsPrimary.Should().BeTrue("because the new membership was not marked as primary");
    }

    [Fact]
    public async Task TerminateMembershipAsync_ShouldSetLeftAt_AndResetPrimaryFlag()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync($"term_{Guid.NewGuid()}@test.com");

        var m = CreateModel(teamId, userId, TeamRole.Player, isPrimary: true);
        await _repository.CreateMembershipWithPolicyAsync(m, AppRole.FullControl);

        // Act
        m.LeftAt = DateTime.UtcNow;
        m.IsPrimary = false;

        var result = await _repository.TerminateMembershipAsync(m);

        // Assert
        result.Should().NotBeNull();
        result.LeftAt.Should().NotBeCloseTo(default, precision: TimeSpan.FromSeconds(1));

        var rawMemberships = await GetRawMembershipsAsync(userId);
        var raw = rawMemberships.First(x => x.Id == m.Id);

        raw.IsPrimary.Should().BeFalse();
        raw.LeftAt.Should().NotBeNull();
    }

    #endregion

    #region Read & Query Tests

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.GetActiveMembershipsByEmailAsync"/> returns 
    /// all active roles for a user and excludes roles from which the user has left.
    /// </summary>
    [Fact]
    public async Task GetActiveMembershipsByEmailAsync_ShouldReturnAllActiveRoles()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var email = $"multi_{Guid.NewGuid()}@test.com";
        var userId = await SeedUserAsync(email);

        // Two active roles
        var m1 = CreateModel(teamId, userId, TeamRole.Player, isPrimary: true);
        var m2 = CreateModel(teamId, userId, TeamRole.Captain, isPrimary: false);
        // One terminated role
        var m3 = CreateModel(teamId, userId, TeamRole.Analyst, isPrimary: false);
        m3.LeftAt = DateTime.UtcNow;

        await _repository.CreateMembershipWithPolicyAsync(m1, AppRole.Viewer);
        await _repository.CreateMembershipWithPolicyAsync(m2, AppRole.Viewer);
        await _repository.TerminateMembershipAsync(m3);

        // Act
        var result = await _repository.GetActiveMembershipsByEmailAsync(teamId, email);

        // Assert
        var activeMemberships = result.ToList();
        activeMemberships.Should().HaveCount(2);
        activeMemberships.Should().Contain(x => x.RoleInTeam == TeamRole.Player);
        activeMemberships.Should().Contain(x => x.RoleInTeam == TeamRole.Captain);
        activeMemberships.Should().NotContain(x => x.RoleInTeam == TeamRole.Analyst);
    }

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.GetActiveMembershipByEmailAndRoleAsync"/>
    /// returns the specific record when it exists.
    /// </summary>
    [Fact]
    public async Task GetActiveMembershipByEmailAndRoleAsync_ShouldReturnSpecificRecord_WhenExists()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var email = $"specific_{Guid.NewGuid()}@test.com";
        var userId = await SeedUserAsync(email);
        var role = TeamRole.HeadCoach;

        var m = CreateModel(teamId, userId, role, isPrimary: true);
        await _repository.CreateMembershipWithPolicyAsync(m, AppRole.FullControl);

        // Act
        var result = await _repository.GetActiveMembershipByEmailAndRoleAsync(teamId, email, role);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(m.Id);
        result.RoleInTeam.Should().Be(role);
    }

    [Fact]
    public async Task GetActiveMembershipByEmailAndRoleAsync_ShouldReturnNull_WhenMembershipIsTerminated()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var email = $"term_check_{Guid.NewGuid()}@test.com";
        var userId = await SeedUserAsync(email);
        var role = TeamRole.Player;

        var m = CreateModel(teamId, userId, role, isPrimary: false);
        m.LeftAt = DateTime.UtcNow;
        await _repository.TerminateMembershipAsync(m);

        // Act
        var result = await _repository.GetActiveMembershipByEmailAndRoleAsync(teamId, email, role);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMembersJsonAsync_ShouldReturnActiveMembers_AndExcludeTerminated()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var user1Id = await SeedUserAsync("active@test.com");
        var user2Id = await SeedUserAsync("left@test.com");

        var mActive = CreateModel(teamId, user1Id, TeamRole.Player, isPrimary: true);
        var mLeft = CreateModel(teamId, user2Id, TeamRole.Player, isPrimary: false);

        await _repository.CreateMembershipWithPolicyAsync(mActive, AppRole.Viewer);
        await _repository.CreateMembershipWithPolicyAsync(mLeft, AppRole.Viewer);

        mLeft.LeftAt = DateTime.UtcNow;
        await _repository.TerminateMembershipAsync(mLeft);

        // Act
        var jsonResult = await _repository.GetMembersJsonAsync(teamId);

        // Assert
        jsonResult.Should().NotBeNullOrWhiteSpace();
        var members = JsonSerializer.Deserialize<List<TeamMemberResponse>>(jsonResult!, new JsonSerializerOptions().GetDefault());

        members.Should().HaveCount(1);
        members![0].MembershipId.Should().Be(mActive.Id);
    }

    #endregion

    #region Constraint Tests

    [Fact]
    public async Task CreateMembershipWithPolicyAsync_DuplicateActiveRole_ShouldThrowException()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync($"dup_{Guid.NewGuid()}@test.com");
        var role = TeamRole.Analyst;

        var first = CreateModel(teamId, userId, role, isPrimary: false);
        await _repository.CreateMembershipWithPolicyAsync(first, AppRole.Editor);

        var second = CreateModel(teamId, userId, role, isPrimary: false);

        // Act & Assert
        var act = () => _repository.CreateMembershipWithPolicyAsync(second, AppRole.Editor);
        var ex = await act.Should().ThrowAsync<Npgsql.PostgresException>();
        ex.Which.SqlState.Should().Be("23505"); // Unique violation
    }

    #endregion

    #region Helpers

    private async Task<string> SeedUserAsync(string email)
    {
        var id = $"auth0|{Guid.NewGuid()}";
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, 'Test User', NOW()) ON CONFLICT DO NOTHING",
            new { id, email });
        return id;
    }

    private async Task<Guid> SeedTeamAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var countryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.countries (name, code, createdat) VALUES (@n, @c, now()) RETURNING id",
            new { n = $"Country_{suffix}", c = suffix[..3].ToUpper() });

        var regionId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.regions (countryid, name) VALUES (@cid, @n) RETURNING id",
            new { cid = countryId, n = $"Region_{suffix}" });

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.cities (id, regionid, name) VALUES (@id, @rid, @n)",
            new { id = cityId, rid = regionId, n = $"City_{suffix}" });

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, name, cityid, createdat) VALUES (@id, @n, @cityId, now())",
            new { id = clubId, n = $"Club_{suffix}", cityId });

        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.sports (id, name) VALUES (@id, @n) ON CONFLICT DO NOTHING",
            new { id = sportId, n = $"Sport_{suffix}" });

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.teams (id, name, clubid, sportid, minbirthyear, gender, createdat) 
              VALUES (@id, @n, @clubId, @sportId, 2010, 0, NOW())",
            new { id = teamId, n = $"Team_{suffix}", clubId, sportId });

        return teamId;
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

    #endregion
}