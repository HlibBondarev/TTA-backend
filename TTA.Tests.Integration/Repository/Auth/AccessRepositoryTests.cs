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
public class AccessRepositoryTests : BaseIntegrationTest
{
    private readonly AccessRepository _repository;

    public AccessRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new AccessRepository(fixture.ConnectionFactory);
    }

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
    /// </summary>
    [Fact]
    public async Task RemoveAccessAsync_ShouldRevokePermission_WhenExpiresAtIsSetToPast()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var targetId = Guid.NewGuid();
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
        createdPolicy.ExpiresAt = creationTime.AddSeconds(1);
        await _repository.RemoveAccessAsync(createdPolicy);

        var role = await _repository.GetUserRoleForScope(userId, TargetScope.Team, targetId);

        // Assert
        role.Should().BeNull("because current time is past the expiresat timestamp");
    }


    /// <summary>
    /// Verifies that adding and then removing an access policy for a non-team scope
    /// (e.g., Club) correctly reflects in the user's effective role.
    /// </summary>
    [Fact]
    public async Task AddAndRemoveAccess_ShouldReflectChanges_ForClubScope()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var clubId = Guid.NewGuid();
        var scope = TargetScope.Club;
        var role = AppRole.FullControl;

        // Use a significant buffer to ensure expiration is recognized by the DB
        var createdAt = DateTime.UtcNow.AddDays(-2);
        var expiresAt = DateTime.UtcNow.AddDays(-1);

        await SeedUserAsync(userId);

        var policy = new AccessPolicy
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetId = clubId,
            TargetType = scope,
            Role = role,
            CreatedAt = createdAt
        };

        // Act - Add Access
        await _repository.AddAccessAsync(policy);

        // Assert - Role should be present
        var roleBefore = await _repository.GetUserRoleForScope(userId, scope, clubId);
        roleBefore.Should().Be(role);

        // Act - Remove Access
        // Setting ExpiresAt to 1 day ago ensures it is definitely in the past for the DB
        policy.ExpiresAt = expiresAt;
        await _repository.RemoveAccessAsync(policy);

        // Assert - Role should now be null because expiresat < NOW() in Postgres
        var roleAfter = await _repository.GetUserRoleForScope(userId, scope, clubId);
        roleAfter.Should().BeNull("because the access policy has been expired/revoked");
    }

    #endregion

    #region GetActiveTeamPolicyAsync Tests

    /// <summary>
    /// Verifies that <see cref="AccessRepository.GetActiveTeamPolicyAsync"/> returns the correct 
    /// policy when an active policy for the specified team exists.
    /// </summary>
    [Fact]
    public async Task GetActiveTeamPolicyAsync_ShouldReturnPolicy_WhenActiveTeamPolicyExists()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var teamId = Guid.NewGuid();
        await SeedUserAsync(userId);

        var policyId = Guid.NewGuid();
        await SeedAccessPolicyAsync(userId, TargetScope.Team, teamId, AppRole.Editor, id: policyId);

        // Act
        var result = await _repository.GetActiveTeamPolicyAsync(userId, teamId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(policyId);
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

        var expiredAt = DateTime.UtcNow.AddMinutes(-10);
        var createdAt = DateTime.UtcNow.AddMinutes(-20);
        await SeedAccessPolicyAsync(userId, TargetScope.Team, teamId, AppRole.Viewer, createdAt, expiredAt);

        // Act
        var result = await _repository.GetActiveTeamPolicyAsync(userId, teamId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region RemoveAccessAsync Tests

    /// <summary>
    /// Verifies that RemoveAccessAsync correctly updates the expiration date 
    /// when executed within a manually managed transaction.
    /// </summary>
    [Fact]
    public async Task RemoveAccessAsync_WithTransaction_ShouldUpdateExpirationDate()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var teamId = Guid.NewGuid();
        var scope = TargetScope.Team;
        var role = AppRole.FullControl;

        var creationDate = DateTime.UtcNow.AddMinutes(-5);
        var terminationDate = DateTime.UtcNow.AddMinutes(-1);

        await SeedUserAsync(userId);
        await SeedAccessPolicyAsync(userId, scope, teamId, role, creationDate);

        var policy = await _repository.GetActiveTeamPolicyAsync(userId, teamId);
        policy.Should().NotBeNull();

        // Act
        using var connection = await Fixture.ConnectionFactory.CreateConnection().OpenAsync();
        using var transaction = connection.BeginTransaction();

        policy!.ExpiresAt = terminationDate;
        await _repository.RemoveAccessAsync(policy, connection, transaction);

        transaction.Commit();

        // Assert
        var updatedPolicy = await _repository.GetActiveTeamPolicyAsync(userId, teamId);
        updatedPolicy.Should().BeNull();
    }

    #endregion

    #region DeleteAsync Tests

    /// <summary>
    /// Verifies that <see cref="AccessRepository.DeleteAsync"/> permanently deletes an existing access policy 
    /// and returns true.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_WhenPolicyExists_ShouldDeletePolicyAndReturnTrue()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";
        var policyId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await SeedUserAsync(userId);
        await SeedAccessPolicyAsync(userId, TargetScope.Team, targetId, AppRole.Editor, id: policyId);

        // Act
        var result = await _repository.DeleteAsync(policyId, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var role = await _repository.GetUserRoleForScope(userId, TargetScope.Team, targetId);
        role.Should().BeNull("because the policy has been permanently deleted");
    }

    /// <summary>
    /// Verifies that <see cref="AccessRepository.DeleteAsync"/> returns false 
    /// when attempting to delete a policy that does not exist in the database.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_WhenPolicyDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        var nonExistentPolicyId = Guid.NewGuid();

        // Act
        var result = await _repository.DeleteAsync(nonExistentPolicyId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Seed Helpers

    /// <summary>
    /// Universal helper to seed access policies with optional parameters for advanced scenarios.
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
    /// Seeds a user into the database with correct column mapping.
    /// </summary>
    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        const string sql = @"INSERT INTO users (id, displayname, email, createdat) 
                            VALUES (@id, @displayname, @email, @createdat) 
                            ON CONFLICT (id) DO NOTHING";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("displayname", "Access Test User");
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");
        cmd.Parameters.AddWithValue("createdat", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}

/// <summary>
/// Extension to simplify connection opening in integration tests.
/// </summary>
public static class ConnectionExtensions
{
    public static async Task<System.Data.IDbConnection> OpenAsync(this System.Data.IDbConnection connection)
    {
        if (connection is NpgsqlConnection npgsqlConn)
        {
            await npgsqlConn.OpenAsync();
        }
        else
        {
            connection.Open();
        }
        return connection;
    }
}