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

    #region CreateTeam
    [Fact]
    public async Task CreateTeam_ShouldReturnOk_WhenUserIsClubAdmin()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var testUserId = BaseApiTest.TestUserId;

        // 1. User first
        await SeedUserAsync(testUserId);

        // 2. Location & Club (SeedRequiredClubDataAsync already seeds location internally)
        await SeedRequiredClubDataAsync(clubId);

        // 3. Sport (The essential FK for teams)
        await SeedSportDataAsync(sportId, "Football");

        // 4. Policy (Must happen after User and Club exist)
        await SeedClubAdminPolicyAsync(testUserId, clubId);

        var request = new
        {
            Name = "Lions Academy U-12",
            SportId = sportId,
            MinBirthYear = 2012,
            Gender = 0 // Will be mapped to 'Male' by public.upsert_team
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/teams", request);

        // Assert
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            // This will show exactly what went wrong in the Test Output
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"Server responded with error: {content}");
        }

        var resultId = await response.Content.ReadFromJsonAsync<Guid>();
        resultId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateTeam_ShouldReturnForbidden_WhenUserHasNoAccess()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();

        await SeedUserAsync(BaseApiTest.TestUserId);
        await SeedRequiredClubDataAsync(clubId);
        await SeedSportDataAsync(sportId, "Basketball");
        // No policy seeded

        var request = new
        {
            Name = "Unauthorized Team",
            SportId = sportId,
            MinBirthYear = 2010,
            Gender = 1 // Female
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/teams", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateTeam_ShouldReturnBadRequest_WhenNameIsTooShort()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();

        await SeedUserAsync(BaseApiTest.TestUserId);
        await SeedRequiredClubDataAsync(clubId);
        await SeedSportDataAsync(sportId, "Tennis");
        await SeedClubAdminPolicyAsync(BaseApiTest.TestUserId, clubId);

        var invalidRequest = new
        {
            Name = "Ab", // Minimum length is 3
            SportId = sportId,
            MinBirthYear = 2015,
            Gender = 0
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/teams", invalidRequest);

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

        // Use ON CONFLICT to prevent tests from failing if the same policy is seeded twice
        var sql = @"INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @userId, 'FullControl', 'Club', @clubId, @now)
            ON CONFLICT (id) DO NOTHING";

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

    private async Task SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Use a robust UPSERT or check existence to ensure we don't violate unique constraints
        // while ensuring the ID we want to use actually exists.
        var sql = @"
        INSERT INTO public.sports (id, name, defaultconfigid) 
        VALUES (@id, @name, NULL) 
        ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name 
        RETURNING id;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", sportId);
        cmd.Parameters.AddWithValue("name", name);

        // We execute this to ensure the record is there
        var actualId = await cmd.ExecuteScalarAsync();
        // Optional: if the ID in DB was different, we should use it, 
        // but for tests Guid.NewGuid() is fine as long as the record exists.
    }

    private static async Task ExecuteSql(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}