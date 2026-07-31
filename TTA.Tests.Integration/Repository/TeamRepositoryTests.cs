using Dapper;
using FluentAssertions;
using Npgsql;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="TeamRepository"/> using a real database container.
/// </summary>
[Collection("DatabaseCollection")]
public class TeamRepositoryTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private readonly TeamRepository _repository = new(fixture.ConnectionFactory);

    /// <summary>
    /// Verifies that a team can be successfully created and then retrieved by Club ID.
    /// </summary>
    [Fact]
    public async Task CreateTeamAsync_ShouldPersistTeam_AndRetrieveItBack()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        // Seed dependencies: Country -> Region -> City -> Club AND Sport
        await SeedTeamDependenciesAsync(clubId, sportId);

        var team = new Team
        {
            Id = teamId,
            ClubId = clubId,
            SportId = sportId,
            Name = "U-18 Boys Elite",
            MinBirthYear = 2008,
            Gender = Gender.Male,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var createdTeam = await _repository.CreateTeamAsync(team, CancellationToken.None);
        var clubTeams = (await _repository.GetByClubIdAsync(clubId, CancellationToken.None)).ToList();

        // Assert
        createdTeam.Should().NotBeNull();
        createdTeam.Name.Should().Be(team.Name);
        createdTeam.Gender.Should().Be(Gender.Male);
        // Added FK assertions
        createdTeam.ClubId.Should().Be(clubId);
        createdTeam.SportId.Should().Be(sportId);

        clubTeams.Should().ContainSingle();
        clubTeams[0].Id.Should().Be(teamId);
        clubTeams[0].MinBirthYear.Should().Be(2008);
        // Added FK assertions for retrieved collection
        clubTeams[0].ClubId.Should().Be(clubId);
        clubTeams[0].SportId.Should().Be(sportId);
    }

    /// <summary>
    /// Verifies that multiple teams can be retrieved for a single club.
    /// </summary>
    [Fact]
    public async Task GetByClubIdAsync_ShouldReturnAllTeamsForSpecificClub()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        await SeedTeamDependenciesAsync(clubId, sportId);

        var team1 = CreateTeamModel(clubId, sportId, "Alpha Team");
        var team2 = CreateTeamModel(clubId, sportId, "Beta Team");

        await _repository.CreateTeamAsync(team1, CancellationToken.None);
        await _repository.CreateTeamAsync(team2, CancellationToken.None);

        // Act
        var result = (await _repository.GetByClubIdAsync(clubId, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "Alpha Team" && t.ClubId == clubId && t.SportId == sportId);
        result.Should().Contain(t => t.Name == "Beta Team" && t.ClubId == clubId && t.SportId == sportId);
    }

    /// <summary>
    /// Verifies that <see cref="TeamRepository.GetByIdAsync"/> returns the correct team 
    /// when a record with the specified ID exists in the database.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_WhenTeamExists_ShouldReturnTeam()
    {
        // Arrange
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        // Seed required parent records (Country -> Region -> City -> Club AND Sport)
        await SeedTeamDependenciesAsync(clubId, sportId);

        var expectedTeam = new Team
        {
            Id = teamId,
            ClubId = clubId,
            SportId = sportId,
            Name = "Arsenal London U-21",
            MinBirthYear = 2003,
            Gender = Gender.Male,
            CreatedAt = DateTime.UtcNow
        };

        // Persist the team first to ensure it can be retrieved
        await _repository.CreateTeamAsync(expectedTeam, CancellationToken.None);

        // Act
        var result = await _repository.GetByIdAsync(teamId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(expectedTeam.Id);
        result.Name.Should().Be(expectedTeam.Name);
        result.ClubId.Should().Be(expectedTeam.ClubId);
        result.SportId.Should().Be(expectedTeam.SportId);
        result.MinBirthYear.Should().Be(expectedTeam.MinBirthYear);
    }

    /// <summary>
    /// Verifies that <see cref="TeamRepository.GetByIdAsync"/> returns null 
    /// when no team record is found for the provided unique identifier.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_WhenTeamDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _repository.GetByIdAsync(nonExistentId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    #region Helper Methods

    /// <summary>
    /// Seeds all necessary database records required for a Team to exist.
    /// Uses explicit transaction to satisfy deferred FK constraints and updated Sport table schema.
    /// </summary>
    private async Task SeedTeamDependenciesAsync(Guid clubId, Guid sportId)
    {
        using var conn = (NpgsqlConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            var cityId = Guid.NewGuid();
            var configId = Guid.NewGuid();

            // 1. Seed Location & Club hierarchy
            await SeedCountryAsync(conn, transaction, 1, "Ukraine", "UKR");
            await SeedRegionAsync(conn, transaction, 1, "Kyiv Region", 1);
            await SeedCityAsync(conn, transaction, cityId, "Kyiv", 1);
            await SeedClubAsync(conn, transaction, clubId, "Test Athletic Club", cityId);

            // 2. Seed Sport & SportConfiguration (Updated for Issue #69 schema)
            await SeedSportAsync(conn, transaction, sportId, "Football", "FB", configId);
            await SeedSportConfigurationAsync(conn, transaction, configId, sportId);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task SeedCountryAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int id, string name, string code)
    {
        var sql = "INSERT INTO public.countries (id, name, code) VALUES (@id, @name, @code) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, code }, tx);
    }

    private static async Task SeedRegionAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int id, string name, int countryId)
    {
        var sql = "INSERT INTO public.regions (id, name, countryid) VALUES (@id, @name, @countryId) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, countryId }, tx);
    }

    private static async Task SeedCityAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, string name, int regionId)
    {
        var sql = "INSERT INTO public.cities (id, name, regionid) VALUES (@id, @name, @regionId) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, regionId }, tx);
    }

    private static async Task SeedClubAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, string name, Guid cityId)
    {
        var sql = "INSERT INTO public.clubs (id, name, cityid, createdat) VALUES (@id, @name, @cityId, NOW()) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, cityId }, tx);
    }

    private static async Task SeedSportAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, string name, string shortName, Guid defaultConfigId)
    {
        var sql = "INSERT INTO public.sports (id, name, shortname, defaultconfigid) VALUES (@id, @name, @shortName, @defaultConfigId) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, shortName, defaultConfigId }, tx);
    }

    private static async Task SeedSportConfigurationAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, Guid sportId)
    {
        var sql = @"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
            VALUES (@id, @sportId, false, 2, 45, '105x68', 25, 11) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, sportId }, tx);
    }

    private static Team CreateTeamModel(Guid clubId, Guid sportId, string name) => new()
    {
        Id = Guid.NewGuid(),
        ClubId = clubId,
        SportId = sportId,
        Name = name,
        Gender = Gender.Female,
        CreatedAt = DateTime.UtcNow
    };

    #endregion
}