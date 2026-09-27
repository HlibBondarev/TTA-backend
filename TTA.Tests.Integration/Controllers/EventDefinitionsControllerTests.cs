using Dapper;
using FluentAssertions;
using Npgsql;
using System.Net;
using TTA.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the <see cref="TTA.WebAPI.Controllers.EventDefinitionsController"/>.
/// Validates soft-deletion operations for custom user-owned Technical and Tactical Action (TTA) event definitions.
/// </summary>
public class EventDefinitionsControllerTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    private const string BaseUrl = "/api/event-definitions/custom";

    #region DeleteCustomEventDefinition Tests

    /// <summary>
    /// Verifies that <c>DELETE /api/event-definitions/custom/{id}</c> returns HTTP 204 No Content
    /// and sets <c>issoftdeleted = true</c> when the custom event definition exists and is owned by the authenticated user.
    /// </summary>
    [Fact]
    public async Task DeleteCustomEventDefinition_ShouldReturnNoContent_WhenCustomEventDefinitionBelongsToUser()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);
        await SeedUserAsync(TestUserId, "creator@test.com", "Creator User");

        var customDefId = await SeedEventDefinitionAsync(sportId, "Custom Tackle", "TAK", isPositive: true, ownerId: TestUserId);

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{customDefId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify soft-deletion persistence in the database
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var isSoftDeleted = await conn.ExecuteScalarAsync<bool>(
            "SELECT issoftdeleted FROM public.eventdefinitions WHERE id = @id",
            new { id = customDefId });

        isSoftDeleted.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <c>DELETE /api/event-definitions/custom/{id}</c> returns HTTP 404 Not Found
    /// when attempting to delete a custom event definition that does not exist in the database.
    /// </summary>
    [Fact]
    public async Task DeleteCustomEventDefinition_ShouldReturnNotFound_WhenEventDefinitionDoesNotExist()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that <c>DELETE /api/event-definitions/custom/{id}</c> returns HTTP 404 Not Found
    /// when attempting to delete a custom event definition owned by a different user.
    /// </summary>
    [Fact]
    public async Task DeleteCustomEventDefinition_ShouldReturnNotFound_WhenEventDefinitionBelongsToAnotherUser()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);

        const string otherUserId = "auth0|other-user";
        await SeedUserAsync(TestUserId, "current@test.com", "Current User");
        await SeedUserAsync(otherUserId, "other@test.com", "Other User");

        var otherUserDefId = await SeedEventDefinitionAsync(sportId, "Other Custom Action", "OCA", isPositive: true, ownerId: otherUserId);

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{otherUserDefId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify entity state remained unchanged in the database
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var isSoftDeleted = await conn.ExecuteScalarAsync<bool>(
            "SELECT issoftdeleted FROM public.eventdefinitions WHERE id = @id",
            new { id = otherUserDefId });

        isSoftDeleted.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <c>DELETE /api/event-definitions/custom/{id}</c> returns HTTP 404 Not Found
    /// when attempting to delete a system default event definition (where ownerId is NULL).
    /// </summary>
    [Fact]
    public async Task DeleteCustomEventDefinition_ShouldReturnNotFound_WhenEventDefinitionIsSystemDefault()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{Guid.NewGuid():N}", "SPT", configId);

        var systemDefId = await SeedEventDefinitionAsync(sportId, "System Goal", "SGL", isPositive: true, ownerId: null);

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{systemDefId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify system definition remained active and unaffected
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        var isSoftDeleted = await conn.ExecuteScalarAsync<bool>(
            "SELECT issoftdeleted FROM public.eventdefinitions WHERE id = @id",
            new { id = systemDefId });

        isSoftDeleted.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <c>DELETE /api/event-definitions/custom/{id}</c> returns HTTP 401 Unauthorized
    /// when an anonymous user attempts to perform a soft-delete operation.
    /// </summary>
    [Fact]
    public async Task DeleteCustomEventDefinition_ShouldReturnUnauthorized_WhenAuthenticationIsDisabled()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        try
        {
            TestAuthHandler.IsEnabled = false;

            // Act
            var response = await Client.DeleteAsync($"{BaseUrl}/{randomId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            TestAuthHandler.IsEnabled = true;
        }
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