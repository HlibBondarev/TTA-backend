using FluentAssertions;
using Npgsql;
using TTA.Common.Enums;
using TTA.DataAccess.Repository.Auth;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository.Auth;

public class AccessRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly AccessRepository _repository = new(fixture.ConnectionFactory);

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

    [Fact]
    public async Task GetUserRoleForScope_ShouldReturnRole_WhenTargetIdIsNull()
    {
        // Arrange
        var userId = $"auth0|{Guid.NewGuid()}";

        // FIX: Use 'Global' scope from your TargetScope enum
        var scope = TargetScope.Global;
        var expectedRole = AppRole.FullControl; // Use FullControl from your AppRole enum

        await SeedUserAsync(userId);
        await SeedAccessPolicyAsync(userId, scope, null, expectedRole);

        // Act
        var result = await _repository.GetUserRoleForScope(userId, scope, null, CancellationToken.None);

        // Assert
        result.Should().Be(expectedRole);
    }

    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = "INSERT INTO public.users (id, displayname, email, createdat) VALUES (@id, @name, @email, @date) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("name", "Access Test User");
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedAccessPolicyAsync(string userId, TargetScope targetType, Guid? targetId, AppRole role)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var sql = @"INSERT INTO auth.accesspolicies (id, userid, targettype, targetid, role, createdat) 
                    VALUES (@id, @uid, @tt, @tid, @r, @dt)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("uid", userId);

        // Npgsql automatically handles Enums as integers if the column is INT
        cmd.Parameters.AddWithValue("tt", (int)targetType);
        cmd.Parameters.AddWithValue("tid", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("r", (int)role);
        cmd.Parameters.AddWithValue("dt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
    }
}