using Dapper;
using FluentAssertions;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.SportConfigurations.DTOs;
using TTA.BusinessLogic.Features.Sports.DTOs;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.SportsController"/>.
/// Validates retrieval of sports, sport configuration profiles, custom event definition management,
/// and user event preset layouts via HTTP API endpoints.
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

    #region GetAvailableEventDefinitions Tests

    /// <summary>
    /// Verifies that <c>GET /api/sports/{sportId}/event-definitions</c> returns HTTP 200 OK
    /// with available system default and custom event definitions when an authenticated user requests them.
    /// </summary>
    [Fact]
    public async Task GetAvailableEventDefinitions_ShouldReturnOkWithDefinitions_WhenUserIsAuthenticated()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);

        await SeedUserAsync(TestUserId, "user@test.com", "Test User");

        var sysDefId = await SeedEventDefinitionAsync(sportId, "Goal", "GL", isPositive: true, ownerId: null);
        var customDefId = await SeedEventDefinitionAsync(sportId, "Custom Assist", "AST", isPositive: true, ownerId: TestUserId);

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{sportId}/event-definitions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<EventDefinitionResponse>>();
        result.Should().NotBeNull();

        var definitions = result!.ToList();
        definitions.Should().HaveCount(2);

        var sysDef = definitions.FirstOrDefault(d => d.Id == sysDefId);
        sysDef.Should().NotBeNull();
        sysDef!.Name.Should().Be("Goal");
        sysDef.IsCustom.Should().BeFalse();

        var customDef = definitions.FirstOrDefault(d => d.Id == customDefId);
        customDef.Should().NotBeNull();
        customDef!.Name.Should().Be("Custom Assist");
        customDef.IsCustom.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <c>GET /api/sports/{sportId}/event-definitions</c> returns HTTP 401 Unauthorized 
    /// when the request is made without authentication credentials.
    /// </summary>
    [Fact]
    public async Task GetAvailableEventDefinitions_ShouldReturnUnauthorized_WhenAuthenticationIsDisabled()
    {
        // Arrange
        var sportId = Guid.NewGuid();

        try
        {
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.GetAsync($"{BaseUrl}/{sportId}/event-definitions");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            TestAuthHandler.IsEnabled = true;
        }
    }

    /// <summary>
    /// Verifies that <see cref="TTA.WebAPI.Controllers.SportsController.GetAvailableEventDefinitions"/> 
    /// returns Response-Cache suppression headers (Cache-Control: no-store) in the HTTP response.
    /// </summary>
    [Fact]
    public async Task GetAvailableEventDefinitions_ShouldIncludeNoStoreCacheHeader_WhenAuthenticated()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);
        await SeedUserAsync(TestUserId, "user@test.com", "Test User");

        // Act
        var response = await Client.GetAsync($"{BaseUrl}/{sportId}/event-definitions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    #endregion

    #region CreateCustomEventDefinition Tests

    /// <summary>
    /// Verifies that <c>POST /api/sports/{sportId}/event-definitions/custom</c> returns HTTP 201 Created
    /// and persists a new custom event definition when provided with valid request data.
    /// </summary>
    [Fact]
    public async Task CreateCustomEventDefinition_ShouldReturnCreated_WhenRequestIsValid()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);
        await SeedUserAsync(TestUserId, "creator@test.com", "Creator User");

        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: "Custom Timeout",
            ShortName: "CTO",
            IsPositive: true
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{sportId}/event-definitions/custom", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<EventDefinitionResponse>();

        result.Should().NotBeNull();
        result!.Id.Should().Be(request.Id);
        result.SportId.Should().Be(sportId);
        result.Name.Should().Be("Custom Timeout");
        result.ShortName.Should().Be("CTO");
        result.IsPositive.Should().BeTrue();
        result.IsCustom.Should().BeTrue();
        result.IsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <c>POST /api/sports/{sportId}/event-definitions/custom</c> returns HTTP 400 Bad Request 
    /// when model validation fails due to empty parameters.
    /// </summary>
    [Fact]
    public async Task CreateCustomEventDefinition_ShouldReturnBadRequest_WhenValidationFails()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var invalidRequest = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: string.Empty,
            ShortName: string.Empty,
            IsPositive: false
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{sportId}/event-definitions/custom", invalidRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that <c>POST /api/sports/{sportId}/event-definitions/custom</c> returns HTTP 401 Unauthorized 
    /// when an anonymous user attempts to create a custom event definition.
    /// </summary>
    [Fact]
    public async Task CreateCustomEventDefinition_ShouldReturnUnauthorized_WhenAuthenticationIsDisabled()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var request = new CreateCustomEventDefinitionRequest(Guid.NewGuid(), "Foul", "FL", false);

        try
        {
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.PostAsJsonAsync($"{BaseUrl}/{sportId}/event-definitions/custom", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            TestAuthHandler.IsEnabled = true;
        }
    }

    /// <summary>
    /// Verifies that <c>POST /api/sports/{sportId}/event-definitions/custom</c> returns HTTP 409 Conflict 
    /// when attempting to create a custom event definition with an identifier of an existing soft-deleted record.
    /// </summary>
    [Fact]
    public async Task CreateCustomEventDefinition_ShouldReturnConflict_WhenReusingSoftDeletedId()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);
        await SeedUserAsync(TestUserId, "creator@test.com", "Creator User");

        var customDefId = await SeedEventDefinitionAsync(sportId, "Deleted Action", "DEL", isPositive: true, ownerId: TestUserId, isSoftDeleted: true);

        var request = new CreateCustomEventDefinitionRequest(
            Id: customDefId,
            Name: "Attempted Reused Action",
            ShortName: "ARA",
            IsPositive: false
        );

        // Act
        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{sportId}/event-definitions/custom", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region SaveUserEventPreset Tests

    /// <summary>
    /// Verifies that <c>PUT /api/sports/{sportId}/event-definitions/preset</c> returns HTTP 200 OK
    /// and persists the user preset layout and ordering when valid event definition IDs are provided.
    /// </summary>
    [Fact]
    public async Task SaveUserEventPreset_ShouldReturnOk_WhenRequestIsValid()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);
        await SeedUserAsync(TestUserId, "preset.user@test.com", "Preset User");

        var defId1 = await SeedEventDefinitionAsync(sportId, "Goal", "GL", isPositive: true, ownerId: null);
        var defId2 = await SeedEventDefinitionAsync(sportId, "Foul", "FL", isPositive: false, ownerId: null);

        var request = new SaveUserEventPresetRequest(new[] { defId1, defId2 });

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{sportId}/event-definitions/preset", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify database persistence for user presets
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var presets = (await conn.QueryAsync<(string UserId, Guid EventDefinitionId, int SortOrder)>(
            "SELECT userid, eventdefinitionid, sortorder FROM public.usereventpresets WHERE userid = @userId",
            new { userId = TestUserId })).ToList();

        presets.Should().HaveCount(2);
        presets.Should().Contain(p => p.EventDefinitionId == defId1 && p.SortOrder == 0);
        presets.Should().Contain(p => p.EventDefinitionId == defId2 && p.SortOrder == 1);
    }

    /// <summary>
    /// Verifies that <c>PUT /api/sports/{sportId}/event-definitions/preset</c> returns HTTP 400 Bad Request 
    /// when the request payload fails model validation rules (e.g., null payload).
    /// </summary>
    [Fact]
    public async Task SaveUserEventPreset_ShouldReturnBadRequest_WhenPayloadIsNull()
    {
        // Arrange
        var sportId = Guid.NewGuid();

        // Act
        var response = await Client.PutAsJsonAsync<SaveUserEventPresetRequest>($"{BaseUrl}/{sportId}/event-definitions/preset", null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that <c>PUT /api/sports/{sportId}/event-definitions/preset</c> returns HTTP 401 Unauthorized 
    /// when an anonymous user attempts to update event presets.
    /// </summary>
    [Fact]
    public async Task SaveUserEventPreset_ShouldReturnUnauthorized_WhenAuthenticationIsDisabled()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var request = new SaveUserEventPresetRequest(new[] { Guid.NewGuid() });

        try
        {
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.PutAsJsonAsync($"{BaseUrl}/{sportId}/event-definitions/preset", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            TestAuthHandler.IsEnabled = true;
        }
    }

    /// <summary>
    /// Verifies that <c>PUT /api/sports/{sportId}/event-definitions/preset</c> returns HTTP 409 Conflict 
    /// when the request contains an invalid, soft-deleted, or unauthorized event definition ID.
    /// </summary>
    [Fact]
    public async Task SaveUserEventPreset_ShouldReturnConflict_WhenEventDefinitionIsInvalidOrUnauthorized()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);
        await SeedUserAsync(TestUserId, "preset.user@test.com", "Preset User");

        var foreignUser = $"auth0|other-user-{Guid.NewGuid():N}";
        await SeedUserAsync(foreignUser, "other@test.com", "Other User");
        var foreignDefId = await SeedEventDefinitionAsync(sportId, "Foreign Action", "FRG", isPositive: true, ownerId: foreignUser);

        var request = new SaveUserEventPresetRequest(new[] { foreignDefId });

        // Act
        var response = await Client.PutAsJsonAsync($"{BaseUrl}/{sportId}/event-definitions/preset", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Helper Seed Methods

    private async Task SeedUserAsync(string userId, string email, string displayName)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, @name, NOW()) ON CONFLICT (id) DO NOTHING";
        await conn.ExecuteAsync(sql, new { id = userId, email, name = displayName });
    }

    private async Task<Guid> SeedEventDefinitionAsync(
        Guid sportId,
        string name,
        string shortName,
        bool isPositive,
        string? ownerId,
        bool isSoftDeleted = false)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        var id = Guid.NewGuid();

        const string sql = @"
            INSERT INTO public.eventdefinitions (id, sportid, ownerid, name, shortname, ispositive, issoftdeleted, createdat)
            VALUES (@id, @sportId, @ownerId, @name, @shortName, @isPositive, @isSoftDeleted, NOW())";

        await conn.ExecuteAsync(sql, new { id, sportId, ownerId, name, shortName, isPositive, isSoftDeleted });
        return id;
    }

    #endregion
}