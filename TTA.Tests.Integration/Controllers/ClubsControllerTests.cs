using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.Common.Enums;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <c>ClubsController</c> verifying API endpoints for club creation,
/// player registration, team creation, authorization policies, and payload validation rules.
/// </summary>
/// <param name="fixture">The shared database fixture providing containerized PostgreSQL access.</param>
/// <param name="output">The test output helper for writing diagnostic logs during execution.</param>
public class ClubsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    #region Create

    /// <summary>
    /// Verifies that creating a club returns <see cref="HttpStatusCode.Created"/> (201) and a valid GUID identifier when the request payload is valid.
    /// </summary>
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

    /// <summary>
    /// Verifies that creating a club with an empty name fails with <see cref="HttpStatusCode.BadRequest"/> (400).
    /// </summary>
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

    /// <summary>
    /// Verifies that calling the club creation endpoint without authorization returns <see cref="HttpStatusCode.Unauthorized"/> (401).
    /// </summary>
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

    /// <summary>
    /// Verifies that an authorized club administrator can successfully register a new player under the target club.
    /// </summary>
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

    /// <summary>
    /// Verifies that player creation returns <see cref="HttpStatusCode.Forbidden"/> (403) when the user lacks an administrative access policy for the target club.
    /// </summary>
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

    /// <summary>
    /// Verifies that player creation returns <see cref="HttpStatusCode.BadRequest"/> (400) when required payload fields fail model validation.
    /// </summary>
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

    /// <summary>
    /// Verifies that an authorized club administrator can successfully create a new team under the specified club.
    /// </summary>
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

    /// <summary>
    /// Verifies that team creation returns <see cref="HttpStatusCode.Forbidden"/> (403) when the user lacks permissions to manage the target club.
    /// </summary>
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

    /// <summary>
    /// Seeds mandatory parent entities (Country, Region, City, Club) required for club-dependent controller endpoints.
    /// </summary>
    /// <param name="clubId">The unique identifier of the club to create.</param>
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

    /// <summary>
    /// Seeds an access policy granting full control (<see cref="AppRole.FullControl"/>) over a club (<see cref="TargetScope.Club"/>) to a user.
    /// </summary>
    /// <param name="userId">The unique identifier of the user receiving administrative access.</param>
    /// <param name="clubId">The target club identifier for the access policy scope.</param>
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

    /// <summary>
    /// Seeds a user entity into the public schema database tables.
    /// </summary>
    /// <param name="userId">The unique identifier of the user to seed.</param>
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

    /// <summary>
    /// Seeds minimal location hierarchy (Country, Region, City) required for club creation tests.
    /// </summary>
    /// <param name="cityId">The unique identifier of the city to seed.</param>
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

    /// <summary>
    /// Seeds a sport and its associated default configuration within an explicit transaction
    /// to satisfy foreign key constraints and mandatory <c>shortname</c> and <c>defaultconfigid</c> columns.
    /// </summary>
    /// <param name="sportId">The target sport identifier.</param>
    /// <param name="name">The display name of the sport.</param>
    /// <returns>A task representing the asynchronous operation, returning the generated or existing sport ID.</returns>
    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var configId = Guid.NewGuid();
        var shortName = name.Length > 10 ? name[..10] : name;

        // 1. Insert Sport (Updated with shortname & defaultconfigid)
        var sportSql = @"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @name, @shortName, @configId) 
            ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name 
            RETURNING id;";

        using (var cmd = new NpgsqlCommand(sportSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", sportId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("shortName", shortName);
            cmd.Parameters.AddWithValue("configId", configId);

            var result = await cmd.ExecuteScalarAsync();
            sportId = (Guid)result!;
        }

        // 2. Insert SportConfiguration
        var configSql = @"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
            VALUES (@configId, @sportId, false, 2, 45, '105x68', 25, 11)
            ON CONFLICT DO NOTHING;";

        using (var cmd = new NpgsqlCommand(configSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("configId", configId);
            cmd.Parameters.AddWithValue("sportId", sportId);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        return sportId;
    }

    /// <summary>
    /// Executes a non-query SQL command against an open Npgsql connection.
    /// </summary>
    /// <param name="conn">The active PostgreSQL connection.</param>
    /// <param name="sql">The raw SQL command text to execute.</param>
    private static async Task ExecuteSql(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}