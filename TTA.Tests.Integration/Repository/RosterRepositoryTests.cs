using Dapper;
using FluentAssertions;
using Npgsql;
using System.Data.Common;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="RosterRepository"/> verifying roster management logic,
/// database constraints, and stored procedure behavior.
/// </summary>
public class RosterRepositoryTests : BaseIntegrationTest
{
    private readonly RosterRepository _repository;

    public RosterRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new RosterRepository(fixture.ConnectionFactory);
    }

    #region Upsert & Constraints Tests

    /// <summary>
    /// Verifies that <see cref="RosterRepository.UpsertRosterItemAsync"/> correctly inserts 
    /// a new player record into the roster.
    /// </summary>
    [Fact]
    public async Task UpsertRosterItemAsync_ShouldPersistNewPlayer()
    {
        // Arrange
        var (tournamentId, teamId, sportId, clubId) = await SeedTournamentContextAsync();
        var playerId = await SeedPlayerAsync(clubId);
        var positionId = await SeedPositionAsync(sportId);

        var rosterEntry = CreateModel(tournamentId, teamId, playerId, positionId, 10);

        // Act
        var result = await _repository.UpsertRosterItemAsync(rosterEntry);

        // Assert
        result.Should().NotBeNull();
        result.Number.Should().Be(10);

        var dbContent = (await GetRawRosterAsync(tournamentId)).ToList();
        dbContent.Should().ContainSingle();

        var row = (IDictionary<string, object>)dbContent.First();
        row["playerid"].Should().Be(playerId);
        row["number"].Should().Be(10);
    }

    /// <summary>
    /// Verifies that updating an existing roster entry (same player, same tournament) 
    /// works as an Upsert via the storage function.
    /// </summary>
    [Fact]
    public async Task UpsertRosterItemAsync_ShouldUpdateExistingEntry_WhenPlayerAlreadyInTournament()
    {
        // Arrange
        var (tournamentId, teamId, sportId, clubId) = await SeedTournamentContextAsync();
        var playerId = await SeedPlayerAsync(clubId);
        var positionId = await SeedPositionAsync(sportId);

        await _repository.UpsertRosterItemAsync(CreateModel(tournamentId, teamId, playerId, positionId, 10));

        // Act: Update jersey number for the same player in the same tournament
        var updatedEntry = CreateModel(tournamentId, teamId, playerId, positionId, 99);
        await _repository.UpsertRosterItemAsync(updatedEntry);

        // Assert
        var dbContent = (await GetRawRosterAsync(tournamentId)).ToList();
        dbContent.Should().ContainSingle();
        var row = (IDictionary<string, object>)dbContent.First();
        row["number"].Should().Be(99);
    }

    /// <summary>
    /// Validates the business logic within the storage function that prevents 
    /// duplicate jersey numbers within the same team.
    /// </summary>
    [Fact]
    public async Task UpsertRosterItemAsync_ShouldThrow_WhenJerseyNumberIsDuplicateInSameTeam()
    {
        // Arrange
        var (tournamentId, teamId, sportId, clubId) = await SeedTournamentContextAsync();
        var posId = await SeedPositionAsync(sportId);

        var playerA = await SeedPlayerAsync(clubId);
        await _repository.UpsertRosterItemAsync(CreateModel(tournamentId, teamId, playerA, posId, 7));

        // Act & Assert
        var playerB = await SeedPlayerAsync(clubId);
        var duplicateNumberEntry = CreateModel(tournamentId, teamId, playerB, posId, 7);

        var act = async () => await _repository.UpsertRosterItemAsync(duplicateNumberEntry);

        // Expecting 23505 (Unique Violation) as raised by the storage function validation block
        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "23505");
    }

    #endregion

    #region Retrieval & Deletion Tests

    /// <summary>
    /// Verifies that <see cref="RosterRepository.GetTeamRosterAsync"/> returns joined data 
    /// including player names and position names.
    /// </summary>
    [Fact]
    public async Task GetTeamRosterAsync_ShouldReturnJoinedData()
    {
        // Arrange
        var (tournamentId, teamId, sportId, clubId) = await SeedTournamentContextAsync();
        var playerId = await SeedPlayerAsync(clubId, "Andriy", "Shevchenko");
        var posId = await SeedPositionAsync(sportId, "Forward", "FW");
        await _repository.UpsertRosterItemAsync(CreateModel(tournamentId, teamId, playerId, posId, 7));

        // Act
        var result = (await _repository.GetTeamRosterAsync(tournamentId, teamId)).ToList();

        // Assert
        result.Should().HaveCount(1);
        var row = (IDictionary<string, object>)result.First();
        row["firstname"].Should().Be("Andriy");
        row["lastname"].Should().Be("Shevchenko");
        row["positionname"].Should().Be("Forward");
        row["number"].Should().Be(7);
    }

    /// <summary>
    /// Verifies that <see cref="RosterRepository.RemovePlayerFromRosterAsync"/> removes the assignment.
    /// </summary>
    [Fact]
    public async Task RemovePlayerFromRosterAsync_ShouldDeleteRecord()
    {
        // Arrange
        var (tournamentId, teamId, sportId, clubId) = await SeedTournamentContextAsync();
        var playerId = await SeedPlayerAsync(clubId);
        var posId = await SeedPositionAsync(sportId);
        await _repository.UpsertRosterItemAsync(CreateModel(tournamentId, teamId, playerId, posId, 1));

        // Act
        await _repository.RemovePlayerFromRosterAsync(tournamentId, teamId, playerId);

        // Assert
        var dbContent = await GetRawRosterAsync(tournamentId);
        dbContent.Should().BeEmpty();
    }

    #endregion

    #region Helpers & Seeding

    /// <summary>
    /// Seeds the full hierarchy required to create a valid Tournament and Team according to 01-Tables.sql.
    /// Uses an explicit transaction to satisfy deferred FK constraints and updated Sport table schema.
    /// </summary>
    private async Task<(Guid tournamentId, Guid teamId, Guid sportId, Guid clubId)> SeedTournamentContextAsync()
    {
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        var unique = Guid.NewGuid().ToString("N")[..8];

        var countryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.countries (name, code, createdat) VALUES (@n, @c, now()) RETURNING id",
            new { n = "Country_" + unique, c = unique[..3].ToUpper() }, transaction: transaction);

        var regionId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO public.regions (countryid, name) VALUES (@countryId, @n) RETURNING id",
            new { countryId, n = "Region_" + unique }, transaction: transaction);

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.cities (id, regionid, name) VALUES (@id, @regionId, @n)",
            new { id = cityId, regionId, n = "City_" + unique }, transaction: transaction);

        var ownerId = "auth0|test-owner-" + unique;
        await conn.ExecuteAsync(
            "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@ownerId, @e, @n, now())",
            new { ownerId, e = $"owner_{unique}@test.com", n = "Owner" + unique }, transaction: transaction);

        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var fullShortName = "S_" + unique;
        var shortName = fullShortName.Length <= 10 ? fullShortName : fullShortName[..10];

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @n, @sn, @cfg)",
            new { id = sportId, n = "Sport_" + unique, sn = shortName, cfg = configId }, transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations 
            (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @sportId, true, 2, 45, 'Standard', 25, 11)",
            new { id = configId, sportId }, transaction: transaction);

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityId, @n, now())",
            new { id = clubId, cityId, n = "Club_" + unique }, transaction: transaction);

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) 
            VALUES (@id, @clubId, @sportId, @n, 0, now())",
            new { id = teamId, clubId, sportId, n = "Team_" + unique }, transaction: transaction);

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sportId, @configId, @cityId, @ownerId, @n, now(), now())",
            new { id = tournamentId, sportId, configId, cityId, ownerId, n = "Tournament_" + unique }, transaction: transaction);

        await transaction.CommitAsync();

        return (tournamentId, teamId, sportId, clubId);
    }

    /// <summary>
    /// Seeds a player record. References homeclubid as per schema.
    /// </summary>
    private async Task<Guid> SeedPlayerAsync(Guid clubId, string fn = "John", string ln = "Doe")
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
            VALUES (@id, @clubId, @fn, @ln, '2000-01-01', 0, now())",
            new { id, clubId, fn, ln });
        return id;
    }

    /// <summary>
    /// Seeds a position definition. Requires shortname as per schema.
    /// </summary>
    private async Task<Guid> SeedPositionAsync(Guid sportId, string name = "Defender", string shortName = "DF")
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            VALUES (@id, @sportId, @name, @shortName)",
            new { id, sportId, name, shortName });
        return id;
    }

    /// <summary>
    /// Queries the playerrosters table directly to verify results.
    /// </summary>
    private async Task<IEnumerable<dynamic>> GetRawRosterAsync(Guid tournamentId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        return await conn.QueryAsync("SELECT * FROM public.playerrosters WHERE tournamentid = @tournamentId", new { tournamentId });
    }

    /// <summary>
    /// Factory method for <see cref="PlayerRoster"/> model.
    /// </summary>
    private static PlayerRoster CreateModel(Guid tId, Guid teamId, Guid pId, Guid posId, int num) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = tId,
        TeamId = teamId,
        PlayerId = pId,
        PositionId = posId,
        Number = num,
        CreatedAt = DateTime.UtcNow
    };

    #endregion
}