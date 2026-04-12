using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.Common.Enums;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

public class ClubsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    #region Create
    [Fact]
    public async Task Create_ShouldReturnCreated_WhenRequestIsValid()
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
        response.StatusCode.Should().Be(HttpStatusCode.Created);

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
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.PostAsJsonAsync("/api/clubs", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            TestAuthHandler.IsEnabled = true;
        }
    }
    #endregion

    #region CreatePlayer
    [Fact]
    public async Task CreatePlayer_ShouldReturnCreated_WhenUserIsClubAdmin()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        await SeedRequiredClubDataAsync(clubId);
        await SeedUserAsync(TestUserId);

        // Seed policy using Enum values
        await SeedClubAdminPolicyAsync(TestUserId, clubId);

        var request = new
        {
            FirstName = "Andriy",
            LastName = "Shevchenko",
            BirthDate = new DateOnly(1976, 9, 29),
            Gender = 0 // Male (Maps to Gender.Male)
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/players", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
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
    public async Task CreateTeam_ShouldReturnCreated_WhenUserIsClubAdmin()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();

        await SeedUserAsync(BaseApiTest.TestUserId);
        await SeedRequiredClubDataAsync(clubId);
        sportId = await SeedSportDataAsync(sportId, "Football");

        await SeedClubAdminPolicyAsync(BaseApiTest.TestUserId, clubId);

        var request = new
        {
            Name = "Lions Academy U-12",
            SportId = sportId,
            MinBirthYear = 2012,
            Gender = 0
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/teams", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateTeam_ShouldReturnForbidden_WhenUserHasNoAccess()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();

        await SeedUserAsync(BaseApiTest.TestUserId);
        await SeedRequiredClubDataAsync(clubId);
        sportId = await SeedSportDataAsync(sportId, "Basketball");

        var request = new
        {
            Name = "Unauthorized Team",
            SportId = sportId,
            MinBirthYear = 2010,
            Gender = 1
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/clubs/{clubId}/teams", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
    #endregion

    #region Helpers for Seeding
    private async Task SeedRequiredClubDataAsync(Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

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

    // FIXED: Now using integer casts for Enum values
    private async Task SeedClubAdminPolicyAsync(string userId, Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var sql = @"INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat) 
            VALUES (@id, @userId, @role, @scope, @clubId, @now)
            ON CONFLICT (id) DO NOTHING";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("userId", userId);

        // CASTING ENUMS TO INT
        cmd.Parameters.AddWithValue("role", (int)AppRole.FullControl);
        cmd.Parameters.AddWithValue("scope", (int)TargetScope.Club);

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

        await ExecuteSql(conn, "INSERT INTO public.countries (id, name, code) VALUES (1, 'Ukraine', 'UA') ON CONFLICT DO NOTHING");
        await ExecuteSql(conn, "INSERT INTO public.regions (id, name, countryid) VALUES (1, 'Test Region', 1) ON CONFLICT DO NOTHING");

        var citySql = "INSERT INTO public.cities (id, name, regionid) VALUES (@id, 'Test City', 1) ON CONFLICT DO NOTHING";
        using var cmd = new NpgsqlCommand(citySql, conn);
        cmd.Parameters.AddWithValue("id", cityId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var sql = @"
        INSERT INTO public.sports (id, name, defaultconfigid) 
        VALUES (@id, @name, NULL) 
        ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name 
        RETURNING id;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", sportId);
        cmd.Parameters.AddWithValue("name", name);

        var result = await cmd.ExecuteScalarAsync();
        return (Guid)result!;
    }

    private static async Task ExecuteSql(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
    #endregion
}