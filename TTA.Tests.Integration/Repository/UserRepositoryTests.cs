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

    /// <summary>
    /// Verifies that <see cref="UserRepository.GetByEmailAsync"/> returns matching users 
    /// when records with the target email exist in the database.
    /// </summary>
    [Fact]
    public async Task GetByEmailAsync_WhenUsersExistWithEmail_ShouldReturnMatchingUsers()
    {
        // Arrange
        var email = $"user_{Guid.NewGuid()}@example.com";
        var user = new User
        {
            Id = "auth0|test-email-" + Guid.NewGuid(),
            DisplayName = "Pylyp Orlyk",
            Email = email,
            CreatedAt = DateTime.UtcNow
        };

        await SeedUserAsync(user);

        // Act
        var result = await _repository.GetByEmailAsync(email, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var foundUser = result.First();
        foundUser.Id.Should().Be(user.Id);
        foundUser.Email.Should().Be(email);
    }

    /// <summary>
    /// Verifies that <see cref="UserRepository.GetByEmailAsync"/> returns an empty collection 
    /// when no user matches the requested email address.
    /// </summary>
    [Fact]
    public async Task GetByEmailAsync_WhenNoUserWithEmail_ShouldReturnEmptyCollection()
    {
        // Arrange
        var nonExistentEmail = $"nonexistent_{Guid.NewGuid()}@example.com";

        // Act
        var result = await _repository.GetByEmailAsync(nonExistentEmail, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="UserRepository.UpsertAsync"/> creates a new user record 
    /// when the specified user ID does not yet exist in the database.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_WhenUserIsNew_ShouldInsertUser()
    {
        // Arrange
        var user = new User
        {
            Id = "auth0|new-user-" + Guid.NewGuid(),
            DisplayName = "Bohdan Khmelnytsky",
            Email = $"bohdan_{Guid.NewGuid()}@example.com",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var upsertedUser = await _repository.UpsertAsync(user, CancellationToken.None);

        // Assert
        upsertedUser.Should().NotBeNull();
        upsertedUser.Id.Should().Be(user.Id);

        var dbUser = await _repository.GetByIdAsync(user.Id, CancellationToken.None);
        dbUser.Should().NotBeNull();
        dbUser!.DisplayName.Should().Be(user.DisplayName);
        dbUser.Email.Should().Be(user.Email);
    }

    /// <summary>
    /// Verifies that <see cref="UserRepository.UpsertAsync"/> updates existing user details 
    /// when a record with the same primary key already exists in the database.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_WhenUserExists_ShouldUpdateUser()
    {
        // Arrange
        var userId = "auth0|existing-user-" + Guid.NewGuid();
        var initialUser = new User
        {
            Id = userId,
            DisplayName = "Old Name",
            Email = $"initial_{Guid.NewGuid()}@example.com",
            CreatedAt = DateTime.UtcNow
        };

        await SeedUserAsync(initialUser);

        var updatedUser = new User
        {
            Id = userId,
            DisplayName = "New Name",
            Email = $"updated_{Guid.NewGuid()}@example.com",
            CreatedAt = initialUser.CreatedAt
        };

        // Act
        var result = await _repository.UpsertAsync(updatedUser, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.DisplayName.Should().Be("New Name");

        var dbUser = await _repository.GetByIdAsync(userId, CancellationToken.None);
        dbUser.Should().NotBeNull();
        dbUser!.DisplayName.Should().Be("New Name");
        dbUser.Email.Should().Be(updatedUser.Email);
    }

    /// <summary>
    /// Verifies that <see cref="UserRepository.DeleteAsync"/> deletes an existing user record 
    /// and returns true.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_WhenUserExists_ShouldDeleteUserAndReturnTrue()
    {
        // Arrange
        var userId = "auth0|to-delete-" + Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            DisplayName = "User To Delete",
            Email = $"delete_{Guid.NewGuid()}@example.com",
            CreatedAt = DateTime.UtcNow
        };

        await SeedUserAsync(user);

        // Act
        var result = await _repository.DeleteAsync(userId, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var dbUser = await _repository.GetByIdAsync(userId, CancellationToken.None);
        dbUser.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="UserRepository.DeleteAsync"/> returns false 
    /// when attempting to delete a user record that does not exist in the database.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_WhenUserDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        var nonExistentId = "auth0|non-existent-" + Guid.NewGuid();

        // Act
        var result = await _repository.DeleteAsync(nonExistentId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #region Helpers for Seeding

    /// <summary>
    /// Helper method to seed a user directly into the database for testing purposes.
    /// </summary>
    private async Task SeedUserAsync(User user)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO public.users (id, displayname, email, createdat) 
            VALUES (@Id, @DisplayName, @Email, @CreatedAt) 
            ON CONFLICT (id) DO NOTHING";

        await conn.ExecuteAsync(sql, user);
    }

    #endregion
}