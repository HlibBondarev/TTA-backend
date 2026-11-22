using FluentAssertions;
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

    #region GetByIdAsync Tests

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

    #endregion

    #region GetBySportIdAsync Tests

    /// <summary>
    /// Verifies that <see cref="SportConfigurationRepository.GetBySportIdAsync"/> returns an empty collection
    /// when no configurations exist for the provided sport identifier.
    /// </summary>
    [Fact]
    public async Task GetBySportIdAsync_ShouldReturnEmptyCollection_WhenNoConfigurationsExistForSport()
    {
        // Act
        var result = await _repository.GetBySportIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="SportConfigurationRepository.GetBySportIdAsync"/> retrieves configuration entities
    /// associated strictly with the specified sport identifier.
    /// </summary>
    [Fact]
    public async Task GetBySportIdAsync_ShouldReturnConfigurations_WhenConfigurationsExistForSport()
    {
        // Arrange
        var targetSportId = Guid.NewGuid();
        var targetConfigId = Guid.NewGuid();
        await SeedSportWithConfigAsync(targetSportId, "Water Polo", "WP", targetConfigId);

        var otherSportId = Guid.NewGuid();
        var otherConfigId = Guid.NewGuid();
        await SeedSportWithConfigAsync(otherSportId, "Basketball", "BB", otherConfigId);

        // Act
        var result = (await _repository.GetBySportIdAsync(targetSportId)).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(targetConfigId);
        result[0].SportId.Should().Be(targetSportId);
        result[0].PeriodsCount.Should().Be(2);
        result[0].PeriodDurationMinutes.Should().Be(45);
        result[0].ActivePlayersLimit.Should().Be(7);
    }

    #endregion

    #region MaxPersonalFouls Tests

    /// <summary>
    /// Verifies that <c>MaxPersonalFouls</c> is returned by both the by-id and by-sport queries,
    /// preserving the default <c>null</c> value when unset.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task GetByIdAndGetBySportIdAsync_ShouldReturnExpectedMaxPersonalFouls(int? expectedMaxPersonalFouls)
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId, $"Sport_{sportId:N}"[..20], "SP", configId);

        if (expectedMaxPersonalFouls.HasValue)
        {
            using var conn = Fixture.ConnectionFactory.CreateConnection();
            await Dapper.SqlMapper.ExecuteAsync(conn,
                "UPDATE public.sportconfigurations SET maxpersonalfouls = @maxFouls WHERE id = @id",
                new { id = configId, maxFouls = expectedMaxPersonalFouls.Value });
        }

        // Act
        var byId = await _repository.GetByIdAsync(configId);
        var bySport = (await _repository.GetBySportIdAsync(sportId)).ToList();

        // Assert
        byId!.MaxPersonalFouls.Should().Be(expectedMaxPersonalFouls);
        bySport.Should().ContainSingle().Which.MaxPersonalFouls.Should().Be(expectedMaxPersonalFouls);
    }

    /// <summary>
    /// Verifies that the CHECK constraint rejects a zero or negative <c>maxpersonalfouls</c>.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Table_ShouldRejectNonPositiveMaxPersonalFouls_WithCheckViolation(int value)
    {
        // Arrange
        var configId = Guid.NewGuid();
        await SeedSportWithConfigAsync(Guid.NewGuid(), "Rugby", "RG", configId);
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // Act
        Func<Task> act = async () => await Dapper.SqlMapper.ExecuteAsync(conn,
            "UPDATE public.sportconfigurations SET maxpersonalfouls = @value WHERE id = @id", new { id = configId, value });

        // Assert
        await act.Should().ThrowAsync<Npgsql.PostgresException>().Where(ex => ex.SqlState == "23514");
    }

    #endregion
}
