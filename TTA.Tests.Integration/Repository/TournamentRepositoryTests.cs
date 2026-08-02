using Dapper;
using FluentAssertions;
using Npgsql;
using System.Data.Common;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="TournamentRepository"/> using a real database container.
/// Verifies the full lifecycle of a tournament and database-level integrity constraints.
/// </summary>
public class TournamentRepositoryTests : BaseIntegrationTest
{
    private readonly TournamentRepository _repository;

    public TournamentRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new TournamentRepository(fixture.ConnectionFactory);
    }

    #region CreateOrUpdate Tests

    /// <summary>
    /// Verifies that a new tournament record is correctly persisted in the database 
    /// when all required foreign keys exist in the public schema.
    /// </summary>
    [Fact]
    public async Task CreateOrUpdate_ShouldPersistNewTournament_WhenDataIsValid()
    {
        // Arrange
        var userId = await SeedUserAsync();
        var cityId = await SeedCityAsync();
        var (sportId, configId) = await SeedSportAndConfigurationAsync();

        var tournament = CreateModel(cityId, sportId, configId, userId, "Autumn Cup 2024");

        // Act
        var result = await _repository.CreateOrUpdate(tournament, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(tournament.Id);
        result.Name.Should().Be("Autumn Cup 2024");
    }

    /// <summary>
    /// Verifies that updating an existing tournament works correctly for the owner.
    /// </summary>
    [Fact]
    public async Task CreateOrUpdate_ShouldUpdateExistingTournament_WhenCalledByOwner()
    {
        // Arrange
        var userId = await SeedUserAsync();
        var cityId = await SeedCityAsync();
        var (sportId, configId) = await SeedSportAndConfigurationAsync();

        var tournament = CreateModel(cityId, sportId, configId, userId, "Original Name");
        await _repository.CreateOrUpdate(tournament, CancellationToken.None);

        // Act
        tournament.Name = "Updated Name";
        var result = await _repository.CreateOrUpdate(tournament, CancellationToken.None);

        // Assert
        result.Name.Should().Be("Updated Name");
        var fromDb = await _repository.GetByIdAsync(tournament.Id, CancellationToken.None);
        fromDb!.Name.Should().Be("Updated Name");
    }

    /// <summary>
    /// Verifies that the database throws an exception if a non-owner tries to update the tournament.
    /// </summary>
    [Fact]
    public async Task CreateOrUpdate_ShouldThrowException_WhenNonOwnerTriesToUpdate()
    {
        // Arrange
        var ownerId = await SeedUserAsync("owner@test.com", "auth0|owner");
        var attackerId = await SeedUserAsync("attacker@test.com", "auth0|attacker");

        var cityId = await SeedCityAsync();
        var (sportId, configId) = await SeedSportAndConfigurationAsync();

        var tournament = CreateModel(cityId, sportId, configId, ownerId, "Secure Tournament");
        await _repository.CreateOrUpdate(tournament, CancellationToken.None);

        // Act
        // Attacker tries to update the tournament by providing their own ID as owner
        tournament.OwnerId = attackerId;
        tournament.Name = "Unauthorized Change";

        // Assert
        var act = async () => await _repository.CreateOrUpdate(tournament, CancellationToken.None);

        // The exception should be thrown by the SQL function
        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.MessageText.Contains("Access denied"));
    }

    #endregion

    #region GetByIdAsync Tests

    /// <summary>
    /// Verifies that GetByIdAsync returns the correct tournament with its data.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnTournament_WhenItExists()
    {
        // Arrange
        var userId = await SeedUserAsync();
        var cityId = await SeedCityAsync();
        var (sportId, configId) = await SeedSportAndConfigurationAsync();

        var tournament = CreateModel(cityId, sportId, configId, userId, "Lookup Tournament");
        await _repository.CreateOrUpdate(tournament, CancellationToken.None);

        // Act
        var result = await _repository.GetByIdAsync(tournament.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(tournament.Id);
        result.OwnerId.Should().Be(userId);
    }

    /// <summary>
    /// Verifies that GetByIdAsync returns null when the tournament does not exist.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenTournamentDoesNotExist()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Seeds a user into the public.users table.
    /// </summary>
    private async Task<string> SeedUserAsync(string email = "test@example.com", string sub = "auth0|test-user")
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@id, @email, @name, @created) ON CONFLICT (id) DO NOTHING",
            new { id = sub, email, name = "Test User", created = DateTime.UtcNow });
        return sub;
    }

    /// <summary>
    /// Seeds geography data required for tournament constraints.
    /// </summary>
    private async Task<Guid> SeedCityAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var countryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.countries (name, code, createdat) VALUES (@name, @code, NOW()) RETURNING id",
            new { name = $"Country_{Guid.NewGuid()}", code = Guid.NewGuid().ToString()[..3].ToUpper() });

        var regionId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.regions (countryid, name) VALUES (@cid, 'Test Region') RETURNING id",
            new { cid = countryId });

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.cities (id, regionid, name) VALUES (@id, @rid, @name)",
            new { id = cityId, rid = regionId, name = "Test City" });
        return cityId;
    }

    /// <summary>
    /// Seeds sport and configuration records atomically within an explicit transaction
    /// to satisfy deferred FK constraints and shortname / defaultconfigid NOT NULL constraints.
    /// </summary>
    private async Task<(Guid SportId, Guid ConfigId)> SeedSportAndConfigurationAsync()
    {
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var shortName = $"S_{sportId:N}"[..10];

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @name, @shortName, @configId)",
            new { id = sportId, name = $"Sport_{sportId:N}", shortName, configId }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations 
            (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @sid, false, 2, 45, 'Standard', 25, 11)",
            new { id = configId, sid = sportId }, tx);

        await tx.CommitAsync();

        return (sportId, configId);
    }

    /// <summary>
    /// Creates a local <see cref="Tournament"/> model instance for testing.
    /// </summary>
    private static Tournament CreateModel(Guid cityId, Guid sportId, Guid configId, string ownerId, string name)
    {
        return new Tournament
        {
            Id = Guid.NewGuid(),
            Name = name,
            CityId = cityId,
            SportId = sportId,
            ConfigurationId = configId,
            OwnerId = ownerId,
            StartDate = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(2),
            CreatedAt = DateTime.UtcNow
        };
    }

    #endregion
}