using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.DataAccess.Enums;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the PlayersController using the real API stack and database container.
/// </summary>
public class PlayersControllerTests(DatabaseFixture fixture) : BaseApiTest(fixture)
{
    [Fact]
    public async Task Create_ShouldReturnOk_WhenRequestIsValid()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        await SeedRequiredClubDataAsync(clubId);

        var request = new
        {
            HomeClubId = clubId,
            FirstName = "Andriy",
            LastName = "Shevchenko",
            BirthDate = new DateOnly(1976, 9, 29),
            Gender = Gender.Male
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/players", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultId = await response.Content.ReadFromJsonAsync<Guid>();
        resultId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_ShouldReturnBadRequest_WhenValidationFails()
    {
        // Arrange
        var request = new
        {
            HomeClubId = Guid.Empty, // Invalid ID
            FirstName = "",           // Empty name should trigger validation error
            LastName = "Test",
            BirthDate = DateOnly.FromDateTime(DateTime.Now),
            Gender = Gender.Male
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/players", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

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

    private static async Task ExecuteSql(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}