using Dapper;
using FluentAssertions;
using Npgsql;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="PlayerRepository"/> using a real database container.
/// </summary>
[Collection("DatabaseCollection")]
public class PlayerRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly PlayerRepository _repository = new(fixture.ConnectionFactory);

    /// <summary>
    /// Verifies that a player can be successfully created and then retrieved by Club ID.
    /// </summary>
    [Fact]
    public async Task CreatePlayerAsync_ShouldPersistPlayer_AndRetrieveItBack()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        // Seed dependencies: Country -> Region -> City -> Club
        await SeedClubDependenciesAsync(clubId);

        // Capture time window before operation
        var beforeInsert = DateTime.UtcNow;

        var player = new Player
        {
            Id = playerId,
            HomeClubId = clubId,
            FirstName = "Andriy",
            LastName = "Shevchenko",
            BirthDate = new DateOnly(1976, 9, 29),
            Gender = Gender.Male,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var createdPlayer = await _repository.CreatePlayerAsync(player, CancellationToken.None);

        // Capture time window after operation
        var afterInsert = DateTime.UtcNow;

        // Retrieve by ClubId
        var playersInClub = (await _repository.GetByClubIdAsync(clubId, CancellationToken.None)).ToList();

        // Assert
        createdPlayer.Should().NotBeNull();
        playersInClub.Should().NotBeEmpty();

        var retrieved = playersInClub.First(p => p.Id == playerId);
        retrieved.FirstName.Should().Be(player.FirstName);
        retrieved.LastName.Should().Be(player.LastName);
        retrieved.HomeClubId.Should().Be(clubId);
        retrieved.BirthDate.Should().Be(player.BirthDate);

        // Verify CreatedAt is within the operation time window
        retrieved.CreatedAt.Should().BeOnOrAfter(beforeInsert).And.BeOnOrBefore(afterInsert);
    }

    /// <summary>
    /// Verifies that multiple players belonging to the same club can be retrieved.
    /// </summary>
    [Fact]
    public async Task GetByClubIdAsync_ShouldReturnAllPlayersInClub()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        await SeedClubDependenciesAsync(clubId);

        var player1 = CreateTestPlayer(Guid.NewGuid(), clubId, "Player", "One");
        var player2 = CreateTestPlayer(Guid.NewGuid(), clubId, "Player", "Two");

        await _repository.CreatePlayerAsync(player1, CancellationToken.None);
        await _repository.CreatePlayerAsync(player2, CancellationToken.None);

        // Act
        var players = (await _repository.GetByClubIdAsync(clubId, CancellationToken.None)).ToList();

        // Assert
        players.Should().HaveCount(2);
        players.Should().Contain(p => p.FirstName == "Player" && p.LastName == "One");
        players.Should().Contain(p => p.FirstName == "Player" && p.LastName == "Two");
    }

    #region Helpers for Seeding

    private static Player CreateTestPlayer(Guid id, Guid clubId, string first, string last) => new()
    {
        Id = id,
        HomeClubId = clubId,
        FirstName = first,
        LastName = last,
        BirthDate = new DateOnly(1990, 1, 1),
        Gender = Gender.Male,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Seeds all necessary club dependencies using a single connection and transaction.
    /// </summary>
    private async Task SeedClubDependenciesAsync(Guid clubId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            var cityId = Guid.NewGuid();
            const int countryId = 380;
            const int regionId = 1;

            await SeedCountryAsync(conn, countryId, "Ukraine", "UA");
            await SeedRegionAsync(conn, regionId, "Kyiv Oblast", countryId);
            await SeedCityAsync(conn, cityId, "Kyiv", regionId);
            await SeedClubAsync(conn, clubId, "Dynamo Kyiv", cityId);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task SeedCountryAsync(NpgsqlConnection conn, int id, string name, string code)
    {
        var sql = "INSERT INTO public.countries (id, name, code) VALUES (@id, @name, @code) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, code });
    }

    private static async Task SeedRegionAsync(NpgsqlConnection conn, int id, string name, int countryId)
    {
        var sql = "INSERT INTO public.regions (id, name, countryid) VALUES (@id, @name, @countryId) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, countryId });
    }

    private static async Task SeedCityAsync(NpgsqlConnection conn, Guid id, string name, int regionId)
    {
        var sql = "INSERT INTO public.cities (id, name, regionid) VALUES (@id, @name, @regionId) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, regionId });
    }

    private static async Task SeedClubAsync(NpgsqlConnection conn, Guid id, string name, Guid cityId)
    {
        var sql = "INSERT INTO public.clubs (id, name, cityid, createdat) VALUES (@id, @name, @cityId, @now) ON CONFLICT (id) DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, cityId, now = DateTime.UtcNow });
    }

    #endregion
}