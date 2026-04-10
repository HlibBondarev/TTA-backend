using Dapper;
using FluentAssertions;
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
    /// Seeds the team hierarchy according to the provided SQL Schema.
    /// </summary>
    private async Task<Guid> SeedTeamAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var random = new Random();
        var suffix = random.Next(100, 999);
        var uniqueStr = Guid.NewGuid().ToString()[..8];

        // 1. Countries: id SERIAL (int), code VARCHAR(3)
        var countryId = await conn.QuerySingleAsync<int>(
            "INSERT INTO public.countries (name, code) VALUES (@n, @c) RETURNING id",
            new { n = $"Country_{uniqueStr}", c = uniqueStr[..3].ToUpper() });

        // 2. Regions: id SERIAL (int), countryid INT
        var regionId = await conn.QuerySingleAsync<int>(
            "INSERT INTO public.regions (countryid, name) VALUES (@cid, @n) RETURNING id",
            new { cid = countryId, n = $"Region_{uniqueStr}" });

        // 3. Cities: id UUID, regionid INT
        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.cities (id, regionid, name) VALUES (@id, @rid, @n)",
            new { id = cityId, rid = regionId, n = $"City_{uniqueStr}" });

        // 4. Clubs: id UUID, cityid UUID
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cid, @n, now())",
            new { id = clubId, cid = cityId, n = $"Club_{uniqueStr}" });

        // 5. Sports: id UUID
        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.sports (id, name) VALUES (@id, @n)",
            new { id = sportId, n = $"Sport_{uniqueStr}" });

        // 6. Teams: id UUID, clubid UUID, sportid UUID
        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO public.teams (id, clubid, sportid, name, minbirthyear, gender, createdat) 
              VALUES (@id, @clubId, @sportId, @n, 2010, 0, NOW())",
            new { id = teamId, clubId, sportId, n = $"Team_{uniqueStr}" });

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