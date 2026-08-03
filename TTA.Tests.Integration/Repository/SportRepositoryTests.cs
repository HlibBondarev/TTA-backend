using FluentAssertions;
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

    #region GetByIdAsync Tests

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

    #endregion

    #region GetAllSportsAsync Tests

    /// <summary>
    /// Verifies that <see cref="SportRepository.GetAllSportsAsync"/> returns an empty collection 
    /// when no sports exist in the database.
    /// </summary>
    [Fact]
    public async Task GetAllSportsAsync_ShouldReturnEmptyCollection_WhenNoSportsExist()
    {
        // Act
        var result = await _repository.GetAllSportsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="SportRepository.GetAllSportsAsync"/> retrieves all sports from the database
    /// ordered alphabetically by name as defined in the storage function.
    /// </summary>
    [Fact]
    public async Task GetAllSportsAsync_ShouldReturnAllSports_WhenSportsExist()
    {
        // Arrange
        var sportId1 = Guid.NewGuid();
        var configId1 = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId1, "Water Polo", "WP", configId1);

        var sportId2 = Guid.NewGuid();
        var configId2 = Guid.NewGuid();
        await SeedSportWithConfigAsync(sportId2, "Basketball", "BB", configId2);

        // Act
        var result = (await _repository.GetAllSportsAsync()).ToList();

        // Assert
        result.Should().HaveCount(2);

        // Verification of alphabetical sorting by name (Basketball -> Water Polo)
        result[0].Id.Should().Be(sportId2);
        result[0].Name.Should().Be("Basketball");
        result[0].ShortName.Should().Be("BB");

        result[1].Id.Should().Be(sportId1);
        result[1].Name.Should().Be("Water Polo");
        result[1].ShortName.Should().Be("WP");
    }

    #endregion
}