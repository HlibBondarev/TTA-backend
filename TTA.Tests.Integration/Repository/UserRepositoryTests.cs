using Dapper;
using FluentAssertions;
using Npgsql;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="UserRepository"/> using a real database container.
/// </summary>
[Collection("DatabaseCollection")]
public class UserRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly UserRepository _repository = new(fixture.ConnectionFactory);

    /// <summary>
    /// Verifies that <see cref="UserRepository.GetByIdAsync"/> returns the correct user 
    /// when a record with the specified ID exists in the database.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_WhenUserExists_ShouldReturnUser()
    {
        // Arrange
        var userId = "auth0|test-integration-user-" + Guid.NewGuid();
        var expectedUser = new User
        {
            Id = userId,
            DisplayName = "Ivan Mazepa",
            Email = "ivan.mazepa@example.com",
            CreatedAt = DateTime.UtcNow
        };

        // Seed the user manually into the database since repository doesn't have Create yet
        await SeedUserAsync(expectedUser);

        // Act
        var result = await _repository.GetByIdAsync(userId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(expectedUser.Id);
        result.DisplayName.Should().Be(expectedUser.DisplayName);
        result.Email.Should().Be(expectedUser.Email);
    }

    /// <summary>
    /// Verifies that <see cref="UserRepository.GetByIdAsync"/> returns null 
    /// when no user record is found for the provided unique identifier.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_WhenUserDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var nonExistentId = "auth0|non-existent-" + Guid.NewGuid();

        // Act
        var result = await _repository.GetByIdAsync(nonExistentId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    #region Helpers for Seeding

    /// <summary>
    /// Helper method to seed a user directly into the database for testing purposes.
    /// </summary>
    private async Task SeedUserAsync(User user)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        // SQL structure matches the standard User model and EntityRepositoryBase expectations
        const string sql = @"
            INSERT INTO public.users (id, displayname, email, createdat) 
            VALUES (@Id, @DisplayName, @Email, @CreatedAt) 
            ON CONFLICT (id) DO NOTHING";

        await conn.ExecuteAsync(sql, user);
    }

    #endregion
}