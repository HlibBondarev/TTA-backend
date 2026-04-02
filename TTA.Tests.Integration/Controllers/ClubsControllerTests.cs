using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Controllers;

public class ClubsControllerTests(DatabaseFixture fixture) : BaseApiTest(fixture)
{
    #region Create
    [Fact]
    public async Task Create_ShouldReturnOk_WhenRequestIsValid()
    {
        // Arrange
        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        await SeedUserAsync(TestUserId);

        var request = new
        {
            Name = "Integration Test Club",
            CityId = cityId
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/clubs", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultId = await response.Content.ReadFromJsonAsync<Guid>();
        resultId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_ShouldReturnBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var request = new { Name = "", CityId = Guid.NewGuid() };

        // Act
        var response = await Client.PostAsJsonAsync("/api/clubs", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ShouldReturnUnauthorized_WhenAuthenticationIsDisabled()
    {
        // Arrange
        var request = new { Name = "No Auth Club", CityId = Guid.NewGuid() };

        try
        {
            // Disable our mock auth handler to simulate a request without a valid token
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.PostAsJsonAsync("/api/clubs", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            // Re-enable to ensure other tests are not affected
            TestAuthHandler.IsEnabled = true;
        }
    }
    #endregion

    #region CreatePlayer
    [Fact]
    public async Task CreatePlayer_ShouldReturnOk_WhenUserIsClubAdmin()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        await SeedRequiredClubDataAsync(clubId);
        await SeedUserAsync(TestUserId);

        // IMPORTANT: Seed the access policy so the ClubAdmin policy passes
        await SeedClubAdminPolicyAsync(TestUserId, clubId);

        var request = new
        {
            FirstName = "Andriy",
            LastName = "Shevchenko",
            BirthDate = new DateOnly(1976, 9, 29),
            Gender = 0 // Male
        };

        // Act - Updated route: /api/clubs/{clubId}/players
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/players", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultId = await response.Content.ReadFromJsonAsync<Guid>();
        resultId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreatePlayer_ShouldReturnForbidden_WhenUserHasNoAccess()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        await SeedRequiredClubDataAsync(clubId);
        await SeedUserAsync(TestUserId);
        // We DO NOT seed the policy here

        var request = new { FirstName = "Ivan", LastName = "Ivanov", BirthDate = new DateOnly(2010, 1, 1), Gender = 0 };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/players", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatePlayer_ShouldReturnBadRequest_WhenValidationFails()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        await SeedRequiredClubDataAsync(clubId);
        await SeedUserAsync(TestUserId);
        await SeedClubAdminPolicyAsync(TestUserId, clubId);

        var invalidRequest = new
        {
            FirstName = "",
            LastName = "Doe",
            BirthDate = new DateOnly(2000, 1, 1),
            Gender = 0
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/players", invalidRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    #endregion

    #region Helpers for Seeding

    private async Task SeedRequiredClubDataAsync(Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Seed dependencies: Country -> Region -> City -> Club
        await ExecuteSql(conn, "INSERT INTO public.countries (id, name, code) VALUES (380, 'Ukraine', 'UA') ON CONFLICT DO NOTHING");
        await ExecuteSql(conn, "INSERT INTO public.regions (id, name, countryid) VALUES (1, 'Kyiv Oblast', 380) ON CONFLICT DO NOTHING");

        var cityId = Guid.NewGuid();
        var citySql = "INSERT INTO public.cities (id, name, regionid) VALUES (@id, 'Kyiv', 1) ON CONFLICT DO NOTHING";
        using (var cmd = new NpgsqlCommand(citySql, conn))
        {
            cmd.Parameters.AddWithValue("id", cityId);
            await cmd.ExecuteNonQueryAsync();
        }

        var clubSql = "INSERT INTO public.clubs (id, name, cityid, createdat) VALUES (@id, 'Dynamo Kyiv', @cityId, @now) ON CONFLICT DO NOTHING";
        using (var cmd = new NpgsqlCommand(clubSql, conn))
        {
            cmd.Parameters.AddWithValue("id", clubId);
            cmd.Parameters.AddWithValue("cityId", cityId);
            cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedClubAdminPolicyAsync(string userId, Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var sql = @"INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
                    VALUES (@id, @userId, 'FullControl', 'Club', @clubId, @now)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("clubId", clubId);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = "INSERT INTO public.users (id, displayname, email, createdat) VALUES (@id, @name, @email, @date) ON CONFLICT (id) DO NOTHING";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("name", "Test User");
        cmd.Parameters.AddWithValue("email", "test@example.com");
        cmd.Parameters.AddWithValue("date", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedRequiredLocationDataAsync(Guid cityId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Seed Country -> Region -> City
        await ExecuteSql(conn, "INSERT INTO public.countries (id, name, code) VALUES (1, 'Ukraine', 'UA') ON CONFLICT DO NOTHING");
        await ExecuteSql(conn, "INSERT INTO public.regions (id, name, countryid) VALUES (1, 'Test Region', 1) ON CONFLICT DO NOTHING");

        var citySql = "INSERT INTO public.cities (id, name, regionid) VALUES (@id, 'Test City', 1) ON CONFLICT DO NOTHING";
        using var cmd = new NpgsqlCommand(citySql, conn);
        cmd.Parameters.AddWithValue("id", cityId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteSql(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}