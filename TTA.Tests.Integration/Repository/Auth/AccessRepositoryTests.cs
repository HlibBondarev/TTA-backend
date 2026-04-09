using FluentAssertions;
using Npgsql;
using TTA.Common.Enums;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Auth;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository.Auth;

/// <summary>
/// Integration tests for <see cref="AccessRepository"/> using a real PostgreSQL container.
/// Verifies permission resolution logic, policy creation, and soft-revocation via expiration.
/// </summary>
/// <param name="fixture">The shared database fixture instance.</param>
public class AccessRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly AccessRepository _repository = new(fixture.ConnectionFactory);

    #region GetUserRoleForScope Tests

    /// <summary>
    /// Verifies that <see cref="AccessRepository.GetUserRoleForScope"/> returns the correct role
    /// when a matching active policy exists in the database.
    /// </summary>
    [Fact]
    public async Task GetUserRoleForScope_ShouldReturnRole_WhenPolicyExists()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var targetId = Guid.NewGuid();
        var scope = TargetScope.Club;
        var expectedRole = AppRole.FullControl;

        await SeedUserAsync(userId);
        await SeedAccessPolicyAsync(userId, scope, targetId, expectedRole);

        // Act
        var result = await _repository.GetUserRoleForScope(userId, scope, targetId, CancellationToken.None);

        // Assert
        result.Should().Be(expectedRole);
    }

    /// <summary>
    /// Verifies that <see cref="AccessRepository.GetUserRoleForScope"/> returns null
    /// if no policy has been defined for the specific user and scope.
    /// </summary>
    [Fact]
    public async Task GetUserRoleForScope_ShouldReturnNull_WhenPolicyDoesNotExist()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var targetId = Guid.NewGuid();

        await SeedUserAsync(userId);

        // Act
        var result = await _repository.GetUserRoleForScope(userId, TargetScope.Club, targetId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that global-level policies (where TargetId is NULL) are correctly resolved.
    /// </summary>
    [Fact]
    public async Task GetUserRoleForScope_ShouldReturnRole_WhenTargetIdIsNull()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var scope = TargetScope.Global;
        var expectedRole = AppRole.FullControl;

        await SeedUserAsync(userId);
        await SeedAccessPolicyAsync(userId, scope, null, expectedRole);

        // Act
        var result = await _repository.GetUserRoleForScope(userId, scope, null, CancellationToken.None);

        // Assert
        result.Should().Be(expectedRole);
    }

    #endregion 


    #region AddAccessAsync and RemoveAccessAsync tests

    /// <summary>
    /// Verifies that <see cref="AccessRepository.AddAccessAsync"/> successfully persists a new policy
    /// and makes it immediately effective for the authorization engine.
    /// </summary>
    [Fact]
    public async Task AddAccessAsync_ShouldMakePermissionActive_WhenPolicyIsCreated()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var targetId = Guid.NewGuid();
        var policy = new AccessPolicy
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetType = TargetScope.Club,
            TargetId = targetId,
            Role = AppRole.Editor,
            CreatedAt = DateTime.UtcNow
        };

        await SeedUserAsync(userId);

        // Act
        await _repository.AddAccessAsync(policy);
        var role = await _repository.GetUserRoleForScope(userId, TargetScope.Club, targetId);

        // Assert
        role.Should().Be(AppRole.Editor);
    }

    /// <summary>
    /// Verifies that setting <c>ExpiresAt</c> to a past timestamp correctly revokes access.
    /// This tests the database constraint and the filtering logic in the storage function.
    /// </summary>
    [Fact]
    public async Task RemoveAccessAsync_ShouldRevokePermission_WhenExpiresAtIsSetToPast()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var targetId = Guid.NewGuid();

        // Use a fixed point in the past for creation to allow expiration in the past without violating DB constraints
        var creationTime = DateTime.UtcNow.AddMinutes(-5);

        var policy = new AccessPolicy
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetType = TargetScope.Team,
            TargetId = targetId,
            Role = AppRole.FullControl,
            CreatedAt = creationTime
        };

        await SeedUserAsync(userId);
        var createdPolicy = await _repository.AddAccessAsync(policy);

        // Act
        // Set expiration slightly after creation but still in the past relative to now.
        // This satisfies: CreatedAt <= ExpiresAt < Current_Timestamp
        createdPolicy.ExpiresAt = creationTime.AddSeconds(1);
        await _repository.RemoveAccessAsync(createdPolicy);

        // The auth.get_user_permission function filters by: (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
        var role = await _repository.GetUserRoleForScope(userId, TargetScope.Team, targetId);

        // Assert
        role.Should().BeNull("because current time is past the expiresat timestamp");
    }

    #endregion

    #region GetActiveTeamPolicyAsync Tests

    /// <summary>
    /// Verifies that <see cref="AccessRepository.GetActiveTeamPolicyAsync"/> returns the correct 
    /// <see cref="AccessPolicy"/> when an active policy for the specified team exists.
    /// </summary>
    [Fact]
    public async Task GetActiveTeamPolicyAsync_ShouldReturnPolicy_WhenActiveTeamPolicyExists()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var teamId = Guid.NewGuid();
        await SeedUserAsync(userId);

        // Seed an active policy (ExpiresAt is NULL)
        var policyId = Guid.NewGuid();
        await SeedAccessPolicyAsync(userId, TargetScope.Team, teamId, AppRole.Editor, id: policyId);

        // Act
        var result = await _repository.GetActiveTeamPolicyAsync(userId, teamId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(policyId);
        result.UserId.Should().Be(userId);
        result.TargetId.Should().Be(teamId);
        result.TargetType.Should().Be(TargetScope.Team);
    }

    /// <summary>
    /// Verifies that <see cref="AccessRepository.GetActiveTeamPolicyAsync"/> returns null 
    /// if the policy exists but has expired.
    /// </summary>
    [Fact]
    public async Task GetActiveTeamPolicyAsync_ShouldReturnNull_WhenPolicyIsExpired()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var teamId = Guid.NewGuid();
        await SeedUserAsync(userId);

        // Seed an expired policy (ExpiresAt is in the past)
        var expiredAt = DateTime.UtcNow.AddMinutes(-10);
        var createdAt = DateTime.UtcNow.AddMinutes(-20);
        await SeedAccessPolicyAsync(userId, TargetScope.Team, teamId, AppRole.Viewer, createdAt, expiredAt);

        // Act
        var result = await _repository.GetActiveTeamPolicyAsync(userId, teamId);

        // Assert
        result.Should().BeNull("because the storage function should filter out expired policies");
    }

    /// <summary>
    /// Verifies that <see cref="AccessRepository.GetActiveTeamPolicyAsync"/> does not return
    /// policies that belong to a different scope (e.g., Club), even if they are active.
    /// </summary>
    [Fact]
    public async Task GetActiveTeamPolicyAsync_ShouldReturnNull_WhenOnlyClubPolicyExists()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var clubId = Guid.NewGuid(); // Different ID type, but used to ensure scope filtering
        await SeedUserAsync(userId);

        // Seed a Club policy instead of a Team policy
        await SeedAccessPolicyAsync(userId, TargetScope.Club, clubId, AppRole.FullControl);

        // Act
        var result = await _repository.GetActiveTeamPolicyAsync(userId, Guid.NewGuid());

        // Assert
        result.Should().BeNull("because the method is specifically looking for Team scope policies (targettype = 2)");
    }

    /// <summary>
    /// Verifies that <see cref="AccessRepository.GetActiveTeamPolicyAsync"/> returns the policy
    /// when it has a future expiration date.
    /// </summary>
    [Fact]
    public async Task GetActiveTeamPolicyAsync_ShouldReturnPolicy_WhenExpiresAtIsInFuture()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var teamId = Guid.NewGuid();
        await SeedUserAsync(userId);

        var futureExpiration = DateTime.UtcNow.AddDays(1);
        await SeedAccessPolicyAsync(userId, TargetScope.Team, teamId, AppRole.Editor, expiresAt: futureExpiration);

        // Act
        var result = await _repository.GetActiveTeamPolicyAsync(userId, teamId);

        // Assert
        result.Should().NotBeNull();
        result!.ExpiresAt.Should().BeCloseTo(futureExpiration, precision: TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Seed Helpers

    /// <summary>
    /// Updated helper to support optional ID and expiration for advanced scenarios.
    /// </summary>
    private async Task SeedAccessPolicyAsync(
        string userId,
        TargetScope targetType,
        Guid? targetId,
        AppRole role,
        DateTime? createdAt = null,
        DateTime? expiresAt = null,
        Guid? id = null)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO auth.accesspolicies (id, userid, targettype, targetid, role, createdat, expiresat) 
            VALUES (@id, @uid, @tt, @tid, @r, @dt, @et)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id ?? Guid.NewGuid());
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("tt", (int)targetType);
        cmd.Parameters.AddWithValue("tid", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("r", (int)role);
        cmd.Parameters.AddWithValue("dt", createdAt ?? DateTime.UtcNow);
        cmd.Parameters.AddWithValue("et", (object?)expiresAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds a user into the database. Uses ON CONFLICT to prevent issues with shared state or repeated calls.
    /// </summary>
    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.users (id, displayname, email, createdat) 
            VALUES (@id, @name, @email, @date) 
            ON CONFLICT (id) DO NOTHING";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("name", "Access Test User");
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Directly seeds an access policy into the auth schema for setup purposes.
    /// </summary>
    private async Task SeedAccessPolicyAsync(string userId, TargetScope targetType, Guid? targetId, AppRole role)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var sql = @"INSERT INTO auth.accesspolicies (id, userid, targettype, targetid, role, createdat) 
                    VALUES (@id, @uid, @tt, @tid, @r, @dt)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("uid", userId);

        // Map enum values to integers as expected by the PostgreSQL schema
        cmd.Parameters.AddWithValue("tt", (int)targetType);
        cmd.Parameters.AddWithValue("tid", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("r", (int)role);
        cmd.Parameters.AddWithValue("dt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}