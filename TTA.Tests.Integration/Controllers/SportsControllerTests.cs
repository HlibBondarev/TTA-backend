using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.SportConfigurations.DTOs;
using TTA.BusinessLogic.Features.Sports.DTOs;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.SportsController"/>.
/// Validates retrieval of sports and sport configuration profiles via HTTP API endpoints.
/// </summary>
public class SportsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/sports";

    #region GetAllSports Tests

    /// <summary>
    /// Verifies that <c>GET /api/sports</c> returns HTTP 200 OK with an empty list 
    /// when no sports exist in the database.
    /// </summary>
    [Fact]
    public async Task GetAllSports_ShouldReturnOkWithEmptyList_WhenNoSportsExist()
    {
        // Act
        var response = await Client.GetAsync(BaseUrl);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<SportResponse>>();
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <c>GET /api/sports</c> returns HTTP 200 OK with all populated sports 
    /// when sport entities exist in the database.
    /// </summary>
    [Fact]
    public async Task GetAllSports_ShouldReturnOkWithSports_WhenSportsExist()
    {
        // Arrange
        var sportId1 = Guid.NewGuid();
        var configId1 = Guid.NewGuid();
        var name1 = $"WaterPolo_{Guid.NewGuid():N}";
        await SeedSportWithConfigAsync(sportId1, name1, "WP1", configId1);

        var sportId2 = Guid.NewGuid();
        var configId2 = Guid.NewGuid();
        var name2 = $"Basketball_{Guid.NewGuid():N}";
        await SeedSportWithConfigAsync(sportId2, name2, "BB1", configId2);

        // Act
        var response = await Client.GetAsync(BaseUrl);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<SportResponse>>();
        result.Should().NotBeNull();

        var sports = result!.ToList();
        sports.Should().HaveCount(2);

        sports.Should().Contain(s => s.Id == sportId1 && s.Name == name1 && s.ShortName == "WP1" && s.DefaultConfigId == configId1);
        sports.Should().Contain(s => s.Id == sportId2 && s.Name == name2 && s.ShortName == "BB1" && s.DefaultConfigId == configId2);
    }

    #endregion

    #region GetConfigurationsBySportId Tests

    /// <summary>
    /// Verifies that <c>GET /api/sports/{sportId}/configurations</c> returns HTTP 200 OK 
    /// with associated configurations when the requested sport exists in the database.
    /// </summary>
    [Fact]
    public async Task GetConfigurationsBySportId_ShouldReturnOkWithConfigurations_WhenSportExists()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var name = $"WaterPolo_{Guid.NewGuid():N}";
        await SeedSportWithConfigAsync(sportId, name, "WP2", configId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{sportId}/configurations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<SportConfigurationResponse>>();
        result.Should().NotBeNull();

        var configs = result!.ToList();
        configs.Should().HaveCount(1);
        configs[0].Id.Should().Be(configId);
        configs[0].SportId.Should().Be(sportId);
        configs[0].PeriodsCount.Should().Be(2);
        configs[0].PeriodDurationMinutes.Should().Be(45);
        configs[0].FieldSize.Should().Be("Standard");
    }

    /// <summary>
    /// Verifies that <c>GET /api/sports/{sportId}/configurations</c> returns HTTP 404 Not Found 
    /// when the specified sport identifier does not exist in the database.
    /// </summary>
    [Fact]
    public async Task GetConfigurationsBySportId_ShouldReturnNotFound_WhenSportDoesNotExist()
    {
        // Arrange
        var nonExistentSportId = Guid.NewGuid();

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{nonExistentSportId}/configurations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}