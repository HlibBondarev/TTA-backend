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

        clubTeams.Should().ContainSingle();
        clubTeams[0].Id.Should().Be(teamId);
        clubTeams[0].MinBirthYear.Should().Be(2008);
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
        result.Should().Contain(t => t.Name == "Alpha Team");
        result.Should().Contain(t => t.Name == "Beta Team");
    }

    #region Helper Methods

    /// <summary>
    /// Seeds all necessary database records required for a Team to exist.
    /// </summary>
    private async Task SeedTeamDependenciesAsync(Guid clubId, Guid sportId)
    {
        using var conn = new NpgsqlConnection(Fixture.ConnectionFactory.CreateConnection().ConnectionString);
        await conn.OpenAsync();

        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // 1. Seed Geography & Club (reusing the same logic as in PlayerRepositoryTests)
            var cityId = Guid.NewGuid();
            await SeedCountryAsync(conn, 1, "Ukraine", "UKR");
            await SeedRegionAsync(conn, 1, "Kyiv Region", 1);
            await SeedCityAsync(conn, cityId, "Kyiv", 1);
            await SeedClubAsync(conn, clubId, "Test Athletic Club", cityId);

            // 2. Seed Sport (required for Team)
            await SeedSportAsync(conn, sportId, "Football");

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
        var sql = "INSERT INTO public.clubs (id, name, cityid, createdat) VALUES (@id, @name, @cityId, NOW()) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name, cityId });
    }

    private static async Task SeedSportAsync(NpgsqlConnection conn, Guid id, string name)
    {
        var sql = "INSERT INTO public.sports (id, name) VALUES (@id, @name) ON CONFLICT DO NOTHING";
        await conn.ExecuteAsync(sql, new { id, name });
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