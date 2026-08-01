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
}