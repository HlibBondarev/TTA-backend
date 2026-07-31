using FluentAssertions;
using Npgsql;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="SportConfigurationRepository"/>.
/// Verifies database retrieval of sport configuration entities using stored procedures.
/// </summary>
[Collection("DatabaseCollection")]
public class SportConfigurationRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly SportConfigurationRepository _repository = new(fixture.ConnectionFactory);

    /// <summary>
    /// Verifies that <see cref="SportConfigurationRepository.GetByIdAsync"/> returns the sport configuration when it exists in the database.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnConfiguration_WhenConfigurationExists()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, "Volleyball", "VB", configId);

        // Act
        var result = await _repository.GetByIdAsync(configId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(configId);
        result.SportId.Should().Be(sportId);
        result.PeriodsCount.Should().Be(2);
        result.PeriodDurationMinutes.Should().Be(45);
        result.FieldSize.Should().Be("Standard");
    }

    /// <summary>
    /// Verifies that <see cref="SportConfigurationRepository.GetByIdAsync"/> returns null when the configuration does not exist.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenConfigurationDoesNotExist()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Seeds a sport and its configuration record atomically within a single transaction
    /// to satisfy the deferred foreign key constraint <c>fk_sports_default_config</c>.
    /// </summary>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="name">The name of the sport.</param>
    /// <param name="shortName">The short code of the sport.</param>
    /// <param name="configId">The unique identifier of the configuration.</param>
    private async Task SeedSportWithConfigAsync(Guid sportId, string name, string shortName, Guid configId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        const string sportSql = @"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@sportId, @name, @shortName, @configId)";

        using (var cmd = new NpgsqlCommand(sportSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("sportId", sportId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("shortName", shortName);
            cmd.Parameters.AddWithValue("configId", configId);
            await cmd.ExecuteNonQueryAsync();
        }

        const string configSql = @"
            INSERT INTO public.sportconfigurations (
                id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit
            ) VALUES (
                @configId, @sportId, false, 2, 45, 'Standard', 25, 11, 7)";

        using (var cmd = new NpgsqlCommand(configSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("configId", configId);
            cmd.Parameters.AddWithValue("sportId", sportId);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}