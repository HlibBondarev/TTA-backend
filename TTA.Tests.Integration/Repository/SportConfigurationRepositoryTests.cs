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
}