using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.DataAccess.Models;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.TournamentsController"/>.
/// Covers end-to-end scenarios including authentication and database persistence.
/// </summary>
// Update the primary constructor to include ITestOutputHelper
public class TournamentsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/tournaments";

    /// <summary>
    /// Verifies that an anonymous user can retrieve a tournament by ID.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnOk_WhenTournamentExists()
    {
        // Arrange
        var userId = await SeedUserAsync(TestUserId);
        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Football");
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, userId, "Championship");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{tournamentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<Tournament>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(tournamentId);
    }

    /// <summary>
    /// Verifies that a 404 status is returned when the tournament does not exist.
    /// </summary>
    [Fact]
    public async Task GetById_ShouldReturnNotFound_WhenTournamentDoesNotExist()
    {
        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that an authorized user can create a new tournament.
    /// </summary>
    [Fact]
    public async Task Create_ShouldReturnCreated_WhenDataIsValid()
    {
        // Arrange
        await SeedUserAsync(TestUserId);
        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Basketball");
        var configId = await SeedConfigurationAsync(sportId);

        var request = new CreateTournamentRequest(
            SportId: sportId,
            ConfigurationId: configId,
            CityId: cityId,
            Name: "Summer Slam 2026",
            StartDate: DateTime.UtcNow.AddDays(10),
            EndDate: DateTime.UtcNow.AddDays(15)
        );

        // Act
        var response = await Client.PostAsJsonAsync(BaseUrl, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<TournamentResponse>();
        result.Should().NotBeNull();
        result!.Name.Should().Be(request.Name);
    }

    /// <summary>
    /// Verifies that an authorized owner can update their tournament.
    /// </summary>
    [Fact]
    public async Task Update_ShouldReturnOk_WhenUserIsOwner()
    {
        // Arrange
        await SeedUserAsync(TestUserId);
        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Tennis");
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, TestUserId, "Old Name");

        var updateRequest = new UpdateTournamentRequest(
            SportId: sportId,
            ConfigurationId: configId,
            CityId: cityId,
            Name: "Updated Tournament Name",
            StartDate: DateTime.UtcNow.AddDays(5),
            EndDate: DateTime.UtcNow.AddDays(7)
        );

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{tournamentId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TournamentResponse>();
        result!.Name.Should().Be("Updated Tournament Name");
    }

    /// <summary>
    /// Verifies that a user cannot update a tournament they do not own.
    /// </summary>
    [Fact]
    public async Task Update_ShouldReturnForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        await SeedUserAsync(TestUserId);
        var otherOwnerId = "auth0|other-user";
        await SeedUserAsync(otherOwnerId);

        var cityId = Guid.NewGuid();
        await SeedRequiredLocationDataAsync(cityId);
        var sportId = await SeedSportDataAsync(Guid.NewGuid(), "Volleyball");
        var configId = await SeedConfigurationAsync(sportId);

        var tournamentId = Guid.NewGuid();
        await SeedTournamentAsync(tournamentId, sportId, configId, cityId, otherOwnerId, "Other's Tournament");

        var updateRequest = new UpdateTournamentRequest(sportId, configId, cityId, "Hacked Name", DateTime.UtcNow, null);

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{tournamentId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #region Helpers

    /// <summary>
    /// Seeds a user record into the database to satisfy foreign key constraints.
    /// </summary>
    /// <param name="userId">The unique identifier of the user (e.g., Auth0 ID).</param>
    /// <returns>The seeded user identifier.</returns>
    private async Task<string> SeedUserAsync(string userId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.users (id, email, displayname, createdat)
            VALUES (@id, @email, @name, @created)
            ON CONFLICT (id) DO UPDATE SET displayname = EXCLUDED.displayname
            RETURNING id";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", $"{userId}@example.com");
        cmd.Parameters.AddWithValue("name", $"Test User {userId}");
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);

        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Seeds a tournament record directly into the database for testing.
    /// </summary>
    private async Task SeedTournamentAsync(Guid id, Guid sportId, Guid configId, Guid cityId, string ownerId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, enddate, createdat)
            VALUES (@id, @sportId, @configId, @cityId, @ownerId, @name, @start, @end, @created)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sportId", sportId);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("cityId", cityId);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("start", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("end", DBNull.Value);
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds sport configuration data required for foreign key constraints.
    /// </summary>
    private async Task<Guid> SeedConfigurationAsync(Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var configId = Guid.NewGuid();
        const string sql = @"
            INSERT INTO public.sportconfigurations 
            (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @sid, false, 2, 45, 'Standard', 25, 11)";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", configId);
        cmd.Parameters.AddWithValue("sid", sportId);
        await cmd.ExecuteNonQueryAsync();
        return configId;
    }

    /// <summary>
    /// Seeds basic location data (Country -> Region -> City).
    /// </summary>
    private async Task SeedRequiredLocationDataAsync(Guid cityId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        await ExecuteSql(conn, "INSERT INTO public.countries (id, name, code) VALUES (1, 'Ukraine', 'UA') ON CONFLICT DO NOTHING");
        await ExecuteSql(conn, "INSERT INTO public.regions (id, name, countryid) VALUES (1, 'Test Region', 1) ON CONFLICT DO NOTHING");

        const string citySql = "INSERT INTO public.cities (id, name, regionid) VALUES (@id, 'Test City', 1) ON CONFLICT DO NOTHING";
        using var cmd = new NpgsqlCommand(citySql, conn);
        cmd.Parameters.AddWithValue("id", cityId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds a sport definition.
    /// </summary>
    private async Task<Guid> SeedSportDataAsync(Guid sportId, string name)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.sports (id, name) VALUES (@id, @name) ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name RETURNING id";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", sportId);
        cmd.Parameters.AddWithValue("name", name);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Utility to execute raw SQL.
    /// </summary>
    private static async Task ExecuteSql(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}