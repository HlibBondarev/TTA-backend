using FluentAssertions;
using Npgsql;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

public class ClubRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly ClubRepository _repository = new(fixture.ConnectionFactory);

    [Fact]
    public async Task CreateWithOwnershipAsync_ShouldInsertClubAndPolicy_WhenUserExists()
    {
        // Arrange
        var userId = $"auth0|test-user-{Guid.NewGuid()}"; // Use unique ID to avoid state pollution
        var clubId = Guid.NewGuid();
        var cityId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

        await SeedUserAsync(userId);

        var club = new Club
        {
            Id = clubId,
            Name = "Integration Test Club",
            CityId = cityId,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var resultId = await _repository.CreateWithOwnershipAsync(club, userId, CancellationToken.None);

        // Assert
        resultId.Should().Be(clubId);

        var hasOwnership = await _repository.HasClubOwnershipAsync(userId, clubId, CancellationToken.None);
        hasOwnership.Should().BeTrue();
    }

    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Note: Using lowercase table name 'users' to match PostgreSQL conventions 
        // and using ON CONFLICT to skip if ID already exists.
        var sql = "INSERT INTO public.users (id, displayname, email, createdat) VALUES (@id, @name, @email, @date) ON CONFLICT (id) DO NOTHING";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("name", "Test User");

        // FIX (Finding #6): Generate unique email based on userId to prevent unique constraint violation
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");

        // Npgsql correctly maps DateTime.UtcNow to TIMESTAMPTZ
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
    }
}