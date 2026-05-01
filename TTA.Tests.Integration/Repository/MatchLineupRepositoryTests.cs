using Dapper;
using FluentAssertions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for <see cref="MatchLineupRepository"/> using a real PostgreSQL container.
/// Provides coverage for CRUD operations and batch processing logic.
/// </summary>
public class MatchLineupRepositoryTests : BaseIntegrationTest
{
    private readonly MatchLineupRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchLineupRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture.</param>
    public MatchLineupRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new MatchLineupRepository(fixture.ConnectionFactory);
    }

    #region Upsert Tests

    /// <summary>
    /// Verifies that a new lineup item can be successfully created.
    /// </summary>
    [Fact]
    public async Task UpsertLineupItemAsync_ShouldCreateNewEntry_WhenDataIsValid()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        var lineup = new MatchLineup
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            Number = 10,
            IsInStartingLineup = true,
            PositionId = positionId
        };

        // Act
        var result = await _repository.UpsertLineupItemAsync(lineup, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(lineup.Id);
    }

    /// <summary>
    /// Verifies that an existing lineup item is updated instead of duplicated.
    /// </summary>
    [Fact]
    public async Task UpsertLineupItemAsync_ShouldUpdateEntry_WhenIdExists()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        var id = Guid.NewGuid();
        var lineup = new MatchLineup
        {
            Id = id,
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            Number = 5,
            IsInStartingLineup = true,
            PositionId = positionId
        };
        await _repository.UpsertLineupItemAsync(lineup);

        // Act
        lineup.Number = 11;
        lineup.IsInStartingLineup = false;
        var result = await _repository.UpsertLineupItemAsync(lineup);

        // Assert
        result.Number.Should().Be(11);
        var fromDb = await _repository.GetByIdAsync(id);
        fromDb!.Number.Should().Be(11);
        fromDb.IsInStartingLineup.Should().BeFalse();
    }

    #endregion

    #region Retrieval Tests

    /// <summary>
    /// Verifies retrieval of all lineup items associated with a specific match.
    /// </summary>
    [Fact]
    public async Task GetByMatchIdAsync_ShouldReturnLineup_WhenEntriesExist()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        await _repository.UpsertLineupItemAsync(new MatchLineup
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            Number = 7,
            IsInStartingLineup = true,
            PositionId = positionId
        });

        // Act
        var result = await _repository.GetByMatchIdAsync(matchId, CancellationToken.None);

        // Assert
        result.Should().NotBeEmpty();
        var first = result.First() as IDictionary<string, object>;
        var numberKey = first!.Keys.FirstOrDefault(k => k.Equals("number", StringComparison.OrdinalIgnoreCase));
        Convert.ToInt32(first[numberKey!]).Should().Be(7);
    }

    /// <summary>
    /// Verifies that detailed lineup information (including player names) is retrieved correctly.
    /// </summary>
    [Fact]
    public async Task GetMatchLineupByIdWithDetailsAsync_ShouldReturnDetails_WhenEntryExists()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        var id = Guid.NewGuid();
        await _repository.UpsertLineupItemAsync(new MatchLineup
        {
            Id = id,
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            Number = 22,
            IsInStartingLineup = true,
            PositionId = positionId
        });

        // Act
        var result = await _repository.GetMatchLineupByIdWithDetailsAsync(id, CancellationToken.None);

        // Assert
        var details = (IDictionary<string, object>)result!;
        var firstName = details.FirstOrDefault(x => x.Key.Equals("firstname", StringComparison.OrdinalIgnoreCase)).Value;
        firstName.Should().Be("John");
    }

    /// <summary>
    /// Verifies basic retrieval by primary key.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity_WhenIdIsValid()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        var id = Guid.NewGuid();
        await _repository.UpsertLineupItemAsync(new MatchLineup
        {
            Id = id,
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            PositionId = positionId,
            Number = 1,
            IsInStartingLineup = true
        });

        // Act
        var result = await _repository.GetByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }

    #endregion

    #region Delete & Batch Tests

    /// <summary>
    /// Verifies that <see cref="MatchLineupRepository.DeleteLineupItemAsync"/> returns true
    /// when a record is successfully removed from the database.
    /// </summary>
    [Fact]
    public async Task DeleteLineupItemAsync_ShouldReturnTrue_WhenEntryIsDeleted()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        var id = Guid.NewGuid();

        await _repository.UpsertLineupItemAsync(new MatchLineup
        {
            Id = id,
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            PositionId = positionId,
            Number = 99,
            IsInStartingLineup = false
        });

        // Act
        var deleted = await _repository.DeleteLineupItemAsync(id, CancellationToken.None);

        // Assert
        deleted.Should().BeTrue();
        var exists = await _repository.GetByIdAsync(id);
        exists.Should().BeNull();
    }

    /// <summary>
    /// Verifies that lineup entries can be bulk-copied from a team's tournament roster.
    /// </summary>
    [Fact]
    public async Task CopyFromRosterAsync_ShouldPopulateLineup_FromTeamRoster()
    {
        // Arrange
        var (matchId, teamId, rosterCount) = await SeedMatchWithTeamRosterAsync(3);

        // Act
        var copiedCount = await _repository.CopyFromRosterAsync(matchId, teamId, CancellationToken.None);

        // Assert
        copiedCount.Should().Be(rosterCount);
        var result = await _repository.GetByMatchIdAsync(matchId);
        result.Count().Should().Be(rosterCount);
    }

    #endregion

    #region Validation Tests

    /// <summary>
    /// Verifies that <see cref="MatchLineupRepository.HasLinkedEventsAsync"/> returns true
    /// when there are game events associated with the specific lineup entry.
    /// </summary>
    [Fact]
    public async Task HasLinkedEventsAsync_ShouldReturnTrue_WhenEventsExist()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        var lineupId = Guid.NewGuid();

        await _repository.UpsertLineupItemAsync(new MatchLineup
        {
            Id = lineupId,
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            PositionId = positionId,
            Number = 10,
            IsInStartingLineup = true
        });

        // Seed a game event linked to this lineup item using corrected schema
        await SeedGameEventAsync(matchId, lineupId);

        // Act
        var result = await _repository.HasLinkedEventsAsync(lineupId, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="MatchLineupRepository.HasLinkedEventsAsync"/> returns false
    /// when no game events are linked to the specific lineup entry.
    /// </summary>
    [Fact]
    public async Task HasLinkedEventsAsync_ShouldReturnFalse_WhenNoEventsExist()
    {
        // Arrange
        var (matchId, playerRosterId, positionId) = await SeedMatchLineupRequirementsAsync();
        var lineupId = Guid.NewGuid();

        await _repository.UpsertLineupItemAsync(new MatchLineup
        {
            Id = lineupId,
            MatchId = matchId,
            PlayerRosterId = playerRosterId,
            PositionId = positionId,
            Number = 10,
            IsInStartingLineup = true
        });

        // Act
        var result = await _repository.HasLinkedEventsAsync(lineupId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Seeding Helpers

    private async Task<(Guid MatchId, Guid PlayerRosterId, Guid PositionId)> SeedMatchLineupRequirementsAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var sub = BaseApiTest.TestUserId;

        var countryId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO countries (name, code, createdat) VALUES (@n, @c, now()) ON CONFLICT (name) DO UPDATE SET code = EXCLUDED.code RETURNING id",
            new { n = "TestCountry", c = "TC" });

        var regionId = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO regions (countryid, name) VALUES (@cid, @n) ON CONFLICT (countryid, name) DO UPDATE SET name = EXCLUDED.name RETURNING id",
            new { cid = countryId, n = "Test Region" });

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO cities (id, regionid, name) VALUES (@id, @rid, @n) ON CONFLICT (id) DO NOTHING",
            new { id = cityId, rid = regionId, n = "Test City" });

        await conn.ExecuteAsync("INSERT INTO users (id, email, displayname, createdat) VALUES (@id, @e, @n, now()) ON CONFLICT (id) DO NOTHING",
            new { id = sub, e = "test@tta.com", n = "Tester" });

        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO sports (id, name) VALUES (@id, 'Soccer') ON CONFLICT DO NOTHING", new { id = sportId });

        var configId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @sportId, true, 2, 45, 'Standard', 25, 11) ON CONFLICT DO NOTHING",
            new { id = configId, sportId });

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sportId, @configId, @cityId, @sub, 'Tournament', now(), now())",
            new { id = tournamentId, sportId, configId, cityId, sub });

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO clubs (id, cityid, name, createdat) VALUES (@id, @cityId, 'Club', now())", new { id = clubId, cityId });

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @clubId, @sportId, 'Team', 0, now())",
            new { id = teamId, clubId, sportId });

        var positionId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES (@id, @sportId, 'Striker', 'ST') ON CONFLICT DO NOTHING",
            new { id = positionId, sportId });

        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
            VALUES (@id, @clubId, 'John', 'Doe', '2000-01-01', 0, now())",
            new { id = playerId, clubId });

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) 
            VALUES (@id, @pid, @tid, @teamid, 10, @posId, now())",
            new { id = rosterId, pid = playerId, tid = tournamentId, teamid = teamId, posId = positionId });

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, now(), now())",
            new { id = matchId, tid = tournamentId, teamid = teamId });

        return (matchId, rosterId, positionId);
    }

    private async Task<(Guid MatchId, Guid TeamId, int RosterCount)> SeedMatchWithTeamRosterAsync(int count)
    {
        var (matchId, _, positionId) = await SeedMatchLineupRequirementsAsync();
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        var match = await conn.QuerySingleAsync<dynamic>("SELECT tournamentid, hometeamid FROM matches WHERE id = @id", new { id = matchId });
        Guid tid = match.tournamentid;
        Guid teamId = match.hometeamid;
        Guid clubId = await conn.ExecuteScalarAsync<Guid>("SELECT clubid FROM teams WHERE id = @teamId", new { teamId });

        for (int i = 1; i < count; i++)
        {
            var pid = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
                VALUES (@id, @clubId, 'Player', @l, '2000-01-01', 0, now())",
                new { id = pid, clubId, l = i.ToString() });

            await conn.ExecuteAsync(@"
                INSERT INTO playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) 
                VALUES (@id, @pid, @tid, @teamid, @num, @posId, now())",
                new { id = Guid.NewGuid(), pid, tid, teamid = teamId, num = i + 20, posId = positionId });
        }

        return (matchId, teamId, count);
    }

    /// <summary>
    /// Seeds a game event record linked to a match lineup entry using the exact database schema.
    /// Ensures all mandatory foreign keys (sportid, eventdefinitionid) are valid.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="lineupId">The unique identifier of the match lineup entry.</param>
    private async Task SeedGameEventAsync(Guid matchId, Guid lineupId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // 1. Retrieve existing sportId from the match to maintain referential integrity
        var matchData = await conn.QuerySingleAsync<dynamic>(
            "SELECT tournamentid FROM public.matches WHERE id = @id",
            new { id = matchId });

        Guid tournamentId = matchData.tournamentid;
        Guid sportId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT sportid FROM public.tournaments WHERE id = @tid",
            new { tid = tournamentId });

        // 2. Insert a valid event definition following the provided DDL
        var eventDefId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
        INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat) 
        VALUES (@id, @sportid, @name, @shortname, @ispositive, now()) 
        ON CONFLICT (id) DO NOTHING",
            new
            {
                id = eventDefId,
                sportid = sportId,
                name = "Goal",
                shortname = "G",
                ispositive = true
            });

        // 3. Insert the game event with correct column names and types
        await conn.ExecuteAsync(@"
        INSERT INTO public.gameevents (
            id, 
            matchid, 
            matchlineupid, 
            eventdefinitionid, 
            periodnumber, 
            eventtimestamp, 
            isleadtogoal, 
            createdat
        ) 
        VALUES (@id, @mid, @lid, @edid, @period, @timestamp, @isLead, now())",
            new
            {
                id = Guid.NewGuid(),
                mid = matchId,
                lid = lineupId,
                edid = eventDefId,
                period = 1,
                timestamp = DateTimeOffset.UtcNow,
                isLead = false
            });
    }

    #endregion
}