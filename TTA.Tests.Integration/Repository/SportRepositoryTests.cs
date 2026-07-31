using FluentAssertions;
using Npgsql;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="SportRepository"/>.
/// Verifies database retrieval of sport entities using stored procedures.
/// </summary>
[Collection("DatabaseCollection")]
public class SportRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly SportRepository _repository = new(fixture.ConnectionFactory);

    /// <summary>
    /// Verifies that <see cref="SportRepository.GetByIdAsync"/> returns the sport entity when it exists in the database.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnSport_WhenSportExists()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var defaultConfigId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, "Basketball", "BB", defaultConfigId);

        // Act
        var result = await _repository.GetByIdAsync(sportId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(sportId);
        result.Name.Should().Be("Basketball");
        result.ShortName.Should().Be("BB");
        result.DefaultConfigId.Should().Be(defaultConfigId);
    }

    /// <summary>
    /// Verifies that <see cref="SportRepository.GetByIdAsync"/> returns null when the sport does not exist.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenSportDoesNotExist()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Seeds a sport and its default configuration atomically within a single transaction
    /// to satisfy the deferred foreign key constraint <c>fk_sports_default_config</c>.
    /// </summary>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="name">The name of the sport.</param>
    /// <param name="shortName">The short code of the sport.</param>
    /// <param name="defaultConfigId">The default configuration identifier.</param>
    private async Task SeedSportWithConfigAsync(Guid sportId, string name, string shortName, Guid defaultConfigId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        const string sportSql = @"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@sportId, @name, @shortName, @defaultConfigId)";

        using (var cmd = new NpgsqlCommand(sportSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("sportId", sportId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("shortName", shortName);
            cmd.Parameters.AddWithValue("defaultConfigId", defaultConfigId);
            await cmd.ExecuteNonQueryAsync();
        }

        const string configSql = @"
            INSERT INTO public.sportconfigurations (
                id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit
            ) VALUES (
                @defaultConfigId, @sportId, false, 2, 45, 'Standard', 25, 11, 7)";

        using (var cmd = new NpgsqlCommand(configSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("defaultConfigId", defaultConfigId);
            cmd.Parameters.AddWithValue("sportId", sportId);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}