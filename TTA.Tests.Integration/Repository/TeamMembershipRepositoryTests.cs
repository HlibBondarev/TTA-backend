using Dapper;
using FluentAssertions;
using System.Data.Common;
using TTA.Common.Enums;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="TeamMembershipRepository"/> using a real database container.
/// Verifies membership lifecycle, primary team rotation logic, and JSON reporting consistency.
/// </summary>
public class TeamMembershipRepositoryTests : BaseIntegrationTest
{
    private readonly TeamMembershipRepository _repository;

    public TeamMembershipRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new TeamMembershipRepository(fixture.ConnectionFactory);
    }

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
        var membership = CreateModel(teamId, userId, TeamRole.Player, true);
        var appRole = AppRole.Editor;

        // Act
        var result = await _repository.CreateMembershipWithPolicyAsync(membership, appRole, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(userId);

        var rawMemberships = await GetRawMembershipsAsync(userId);
        rawMemberships.Should().ContainSingle(m => m.TeamId == teamId);
    }

    /// <summary>
    /// Tests that the repository correctly updates an existing membership record, 
    /// specifically changing the primary status of the team for a user.
    /// </summary>
    [Fact]
    public async Task UpdateMembership_ShouldChangePrimaryStatus()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync($"primary_{Guid.NewGuid()}@test.com");
        var membership = CreateModel(teamId, userId, TeamRole.Player, true);
        var created = await _repository.CreateMembershipWithPolicyAsync(membership, AppRole.Viewer);

        // Act
        created.IsPrimary = false;
        await _repository.TerminateMembershipAsync(created, CancellationToken.None);

        // Assert
        var raw = (await GetRawMembershipsAsync(userId)).First();
        raw.IsPrimary.Should().BeFalse();
    }

    #endregion

    #region Retrieval Tests

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.GetActiveMembershipByEmailAndRoleAsync"/> 
    /// returns the correct record when an active membership with the specified role exists.
    /// </summary>
    [Fact]
    public async Task GetActiveMembershipByEmailAndRoleAsync_ShouldReturnMembership_WhenExists()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var email = $"active_{Guid.NewGuid()}@test.com";
        var userId = await SeedUserAsync(email);
        var role = TeamRole.HeadCoach;

        var membership = CreateModel(teamId, userId, role, false);
        await _repository.CreateMembershipWithPolicyAsync(membership, AppRole.Viewer);

        // Act
        var result = await _repository.GetActiveMembershipByEmailAndRoleAsync(teamId, email, role, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(userId);
        result.RoleInTeam.Should().Be(role);
    }

    /// <summary>
    /// Ensures that <see cref="TeamMembershipRepository.GetActiveMembershipByEmailAndRoleAsync"/> 
    /// returns null when no matching active membership is found.
    /// </summary>
    [Fact]
    public async Task GetActiveMembershipByEmailAndRoleAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetActiveMembershipByEmailAndRoleAsync(Guid.NewGuid(), "none@test.com", TeamRole.HeadCoach);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.GetActiveMembershipsByEmailAsync"/> 
    /// returns all memberships for a user that haven't been terminated (LeftAt is NULL).
    /// </summary>
    [Fact]
    public async Task GetActiveMembershipsByEmailAsync_ShouldReturnOnlyActiveRecords()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var email = $"multi_{Guid.NewGuid()}@test.com";
        var userId = await SeedUserAsync(email);

        var active = CreateModel(teamId, userId, TeamRole.Player, true);
        await _repository.CreateMembershipWithPolicyAsync(active, AppRole.Viewer);

        // Act
        var results = await _repository.GetActiveMembershipsByEmailAsync(teamId, email, CancellationToken.None);

        // Assert
        results.Should().ContainSingle();
        results.First().LeftAt.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.GetActiveMembershipsByEmailAsync"/> 
    /// returns an empty collection when no active memberships match the criteria.
    /// </summary>
    [Fact]
    public async Task GetActiveMembershipsByEmailAsync_ShouldReturnEmpty_WhenNoMatches()
    {
        // Act
        var results = await _repository.GetActiveMembershipsByEmailAsync(Guid.NewGuid(), "none@test.com");

        // Assert
        results.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.GetMembersJsonAsync"/> returns a valid JSON string
    /// containing aggregated member details for the specified team.
    /// </summary>
    [Fact]
    public async Task GetMembersJsonAsync_ShouldReturnValidJson_WhenMembersExist()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync($"member_{Guid.NewGuid()}@test.com");
        var membership = CreateModel(teamId, userId, TeamRole.Player, true);
        await _repository.CreateMembershipWithPolicyAsync(membership, AppRole.Viewer);

        // Act
        var jsonResult = await _repository.GetMembersJsonAsync(teamId, CancellationToken.None);

        // Assert
        jsonResult.Should().NotBeNullOrEmpty();
        jsonResult.Should().Contain(userId);
    }

    #endregion

    #region Termination Tests

    /// <summary>
    /// Verifies that <see cref="TeamMembershipRepository.TerminateMembershipAsync"/> 
    /// correctly sets the termination timestamp (soft-delete) in the database.
    /// </summary>
    [Fact]
    public async Task TerminateMembershipAsync_ShouldSetLeftAtDate()
    {
        // Arrange
        var teamId = await SeedTeamAsync();
        var userId = await SeedUserAsync($"term_{Guid.NewGuid()}@test.com");
        var membership = CreateModel(teamId, userId, TeamRole.Player, true);
        var created = await _repository.CreateMembershipWithPolicyAsync(membership, AppRole.Viewer);

        var terminationDate = DateTime.UtcNow;
        created.LeftAt = terminationDate;

        // Act
        await _repository.TerminateMembershipAsync(created, CancellationToken.None);

        // Assert
        var raw = (await GetRawMembershipsAsync(userId)).First();
        raw.LeftAt.Should().NotBeNull();
        raw.LeftAt.Value.Should().BeCloseTo(terminationDate, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Helpers

    private async Task<string> SeedUserAsync(string email)
    {
        var userId = $"auth0|{Guid.NewGuid()}";
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @e, @n, now())",
            new { id = userId, e = email, n = "TestUser" });
        return userId;
    }

    /// <summary>
    /// Helper method to seed base entities (Geography, User, Sport, Configuration, Club, Team) required for testing team memberships.
    /// Uses explicit transaction to satisfy deferred FK constraints and updated Sport table schema.
    /// </summary>
    /// <returns>The unique identifier of the seeded team entity.</returns>
    private async Task<Guid> SeedTeamAsync()
    {
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..6];

        // 1. Geography Setup
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) 
            SELECT 'Ukraine', 'UA' WHERE NOT EXISTS (SELECT 1 FROM public.countries WHERE name = 'Ukraine')",
            transaction: transaction);
        var countryId = await conn.QuerySingleAsync<int>("SELECT id FROM public.countries WHERE name = 'Ukraine'", transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.regions (countryid, name) 
            SELECT @cid, 'Dnipro Region' WHERE NOT EXISTS (SELECT 1 FROM public.regions WHERE name = 'Dnipro Region' AND countryid = @cid)",
            new { cid = countryId }, transaction: transaction);
        var regionId = await conn.QuerySingleAsync<int>("SELECT id FROM public.regions WHERE name = 'Dnipro Region' AND countryid = @cid", new { cid = countryId }, transaction: transaction);

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.cities (id, regionid, name) 
            SELECT @id, @rid, 'Dnipro' WHERE NOT EXISTS (SELECT 1 FROM public.cities WHERE id = @id)",
            new { id = cityId, rid = regionId }, transaction: transaction);

        // 2. Sport & SportConfiguration (Updated for Issue #69 schema)
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var shortName = $"S_{sportId:N}"[..10];

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @n, @sn, @cfg)",
            new { id = sportId, n = $"Sport_{suffix}", sn = shortName, cfg = configId }, transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @s, false, 2, 45, '105x68', 25, 11)",
            new { id = configId, s = sportId }, transaction: transaction);

        // 3. Club & Team
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cid, @name, NOW())",
            new { id = clubId, cid = cityId, name = $"Club_{suffix}" }, transaction: transaction);

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) 
            VALUES (@id, @cid, @sid, @name, 0, NOW())",
            new { id = teamId, cid = clubId, sid = sportId, name = $"Team_{suffix}" }, transaction: transaction);

        await transaction.CommitAsync();

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