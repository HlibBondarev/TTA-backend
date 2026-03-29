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
        var userId = "auth0|test-integration-user";
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

        var sql = "INSERT INTO public.Users (Id, DisplayName, Email, CreatedAt) VALUES (@id, @name, @email, @date) ON CONFLICT (Id) DO NOTHING";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("name", "Test User");
        cmd.Parameters.AddWithValue("email", "test@test.com");
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
    }
}