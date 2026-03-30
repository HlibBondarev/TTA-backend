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
        var userId = $"auth0|test-user-{Guid.NewGuid()}";
        var clubId = Guid.NewGuid();
        var cityId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

        const int countryId = 1;
        const int regionId = 99;

        // Seed dependency chain
        await SeedUserAsync(userId);
        await SeedCountryAsync(countryId, "Ukraine", "UA"); // Додано код країни
        await SeedRegionAsync(regionId, "Test Region", countryId);
        await SeedCityAsync(cityId, "Test City", regionId);

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

    [Fact]
    public async Task CreateWithOwnershipAsync_ShouldThrowArgumentNullException_WhenClubIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _repository.CreateWithOwnershipAsync(null!, "any-user", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task CreateWithOwnershipAsync_ShouldThrowArgumentException_WhenUserIdIsInvalid(string? invalidUserId)
    {
        // Arrange
        var club = new Club { Id = Guid.NewGuid(), Name = "Test", CityId = Guid.NewGuid() };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repository.CreateWithOwnershipAsync(club, invalidUserId!, CancellationToken.None));
    }

    [Fact]
    public async Task HasClubOwnershipAsync_ShouldReturnFalse_WhenUserHasNoPermissions()
    {
        // Arrange
        var userId = $"user-{Guid.NewGuid()}";
        var clubId = Guid.NewGuid();
        await SeedUserAsync(userId);

        // Act
        var result = await _repository.HasClubOwnershipAsync(userId, clubId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasClubOwnershipAsync_ShouldReturnFalse_WhenUserHasDifferentRole()
    {
        // Arrange
        var userId = $"user-{Guid.NewGuid()}";
        var clubId = Guid.NewGuid();
        await SeedUserAsync(userId);

        await SeedAccessPolicyAsync(userId, "Club", clubId, "Editor");

        // Act
        var result = await _repository.HasClubOwnershipAsync(userId, clubId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasExistingClubOwnershipAsync_ShouldReturnFalse_WhenUserDoesNotOwnAnyClub()
    {
        // Arrange
        var userId = $"user-{Guid.NewGuid()}";
        await SeedUserAsync(userId);

        // Act
        var result = await _repository.HasExistingClubOwnershipAsync(userId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = "INSERT INTO public.users (id, displayname, email, createdat) VALUES (@id, @name, @email, @date) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("name", "Test User");
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedCountryAsync(int countryId, string name, string code)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        // Додано стовпець code
        var sql = "INSERT INTO public.countries (id, name, code) VALUES (@id, @name, @code) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", countryId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("code", code);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedRegionAsync(int regionId, string name, int countryId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = "INSERT INTO public.regions (id, name, countryid) VALUES (@id, @name, @cid) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", regionId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("cid", countryId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedCityAsync(Guid cityId, string cityName, int regionId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = "INSERT INTO public.cities (id, name, regionid) VALUES (@id, @name, @rid) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", cityId);
        cmd.Parameters.AddWithValue("name", cityName);
        cmd.Parameters.AddWithValue("rid", regionId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedAccessPolicyAsync(string userId, string targetType, Guid targetId, string role)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = @"INSERT INTO auth.accesspolicies (userid, targettype, targetid, role, createdat) 
                    VALUES (@uid, @tt, @tid, @r, @dt)";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("tt", targetType);
        cmd.Parameters.AddWithValue("tid", targetId);
        cmd.Parameters.AddWithValue("r", role);
        cmd.Parameters.AddWithValue("dt", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }
}