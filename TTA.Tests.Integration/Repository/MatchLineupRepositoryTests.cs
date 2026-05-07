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
    // Class-level static counter – thread-safe, ever-increasing across the test run.
    // Starts high enough to never clash with the jersey 10 seeded by SeedMatchLineupRequirementsAsync.
    private static int _playerNumberSeed = 1000;

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
    /// Verifies that only the selected players from the roster are copied to the match lineup.
    /// Accounts for the 2 automatic placeholders (Home/Guest) created by the database trigger.
    /// </summary>
    [Fact]
    public async Task CopyFromRosterAsync_ShouldCopyOnlySelectedPlayers()
    {
        // Arrange
        var (matchId, playerRosterId, _) = await SeedMatchLineupRequirementsAsync();

        // Get context from the database for additional seeding
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var context = await conn.QuerySingleAsync<(Guid TournamentId, Guid TeamId)>(
            "SELECT tournamentid, teamid FROM public.playerrosters WHERE id = @id",
            new { id = playerRosterId });

        var allRosterIds = await SeedPlayerRostersAsync(context.TournamentId, context.TeamId, 3);
        var selectedPlayerIds = allRosterIds.Take(2).ToList();

        var initialCount = await GetLineupCountAsync(matchId);

        // Act
        foreach (var rosterId in selectedPlayerIds)
        {
            await _repository.CopyFromRosterAsync(matchId, context.TeamId, new List<Guid> { rosterId });
        }

        // Assert
        var finalCount = await GetLineupCountAsync(matchId);
        finalCount.Should().Be(initialCount + selectedPlayerIds.Count);
    }

    /// <summary>
    /// Verifies that zero records are inserted when an empty list of IDs is provided.
    /// </summary>
    [Fact]
    public async Task CopyFromRosterAsync_ShouldReturnZero_WhenSelectionIsEmpty()
    {
        // Arrange
        var (matchId, teamId, _) = await SeedMatchAsync();

        // Act
        var result = await _repository.CopyFromRosterAsync(matchId, teamId, Enumerable.Empty<Guid>());

        // Assert
        result.Should().Be(0);
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

    /// <summary>
    /// Seeds a match and all mandatory dependencies strictly following 01-Tables.sql schema.
    /// Ensures that parent records (User, Country, Region, City, Sport, Teams) exist before dependent ones.
    /// </summary>
    private async Task<(Guid MatchId, Guid TeamId, Guid TournamentId)> SeedMatchAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var guestTeamId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var userId = "test-owner";

        // 1. User (Tournament Owner)
        await conn.ExecuteAsync(@"
            INSERT INTO users (id, email, displayname, createdat) 
            VALUES (@userId, 'owner@test.com', 'Tournament Owner', now()) 
            ON CONFLICT (id) DO NOTHING", new { userId });

        // 2. Geography
        await conn.ExecuteAsync("INSERT INTO countries (name, code) VALUES ('TestCountry', 'TC') ON CONFLICT (code) DO NOTHING");
        var countryId = await conn.ExecuteScalarAsync<int>("SELECT id FROM countries WHERE code = 'TC'");

        await conn.ExecuteAsync("INSERT INTO regions (countryid, name) VALUES (@countryId, 'TestRegion') ON CONFLICT (countryid, name) DO NOTHING", new { countryId });
        var regionId = await conn.ExecuteScalarAsync<int>("SELECT id FROM regions WHERE countryid = @countryId AND name = 'TestRegion'", new { countryId });

        await conn.ExecuteAsync("INSERT INTO cities (id, regionid, name) VALUES (@cityId, @regionId, 'TestCity') ON CONFLICT (regionid, name) DO NOTHING", new { cityId, regionId });
        var effectiveCityId = await conn.ExecuteScalarAsync<Guid>("SELECT id FROM cities WHERE regionid = @regionId AND name = 'TestCity'", new { regionId });

        // 3. Sport & Config
        await conn.ExecuteAsync("INSERT INTO sports (id, name) VALUES (@sportId, 'Football')", new { sportId });
        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
            VALUES (@configId, @sportId, false, 2, 45, 'Standard', 25, 11)",
            new { configId, sportId });

        // 4. Tournament
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@tournamentId, @sportId, @configId, @effectiveCityId, @userId, 'Test Tournament', now(), now())",
            new { tournamentId, sportId, configId, effectiveCityId, userId });

        // 5. Club
        await conn.ExecuteAsync("INSERT INTO clubs (id, cityid, name, createdat) VALUES (@clubId, @effectiveCityId, 'Test Club', now())",
            new { clubId, effectiveCityId });

        // 6. Home and Guest Teams (Both must exist for the match)
        await conn.ExecuteAsync(@"
            INSERT INTO teams (id, clubid, sportid, name, gender, createdat) 
            VALUES (@teamId, @clubId, @sportId, 'Home Team', 0, now())",
            new { teamId, clubId, sportId });

        await conn.ExecuteAsync(@"
            INSERT INTO teams (id, clubid, sportid, name, gender, createdat) 
            VALUES (@guestTeamId, @clubId, @sportId, 'Guest Team', 0, now())",
            new { guestTeamId, clubId, sportId });

        // 7. Match (Using valid GuestTeamId)
        await conn.ExecuteAsync(@"
            INSERT INTO matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat)
            VALUES (@matchId, @tournamentId, @teamId, @guestTeamId, now(), now())",
            new { matchId, tournamentId, teamId, guestTeamId });

        return (matchId, teamId, tournamentId);
    }

    /// <summary>
    /// Seeds player records and tournament roster entries following the schema.
    /// </summary>
    private async Task<List<Guid>> SeedPlayerRostersAsync(Guid tournamentId, Guid teamId, int count)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var ids = new List<Guid>();

        // We intentionally read sportId from the tournaments table to ensure that 
        // the seeded playerpositiondefinitions are perfectly aligned with the tournament's sport.
        // This maintains a valid FK chain into playerrosters.positionid. While teams also 
        // store sportid, we use tournaments here for seeding consistency.
        var sportId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT sportid FROM public.tournaments WHERE id = @tournamentId",
            new { tournamentId });

        // Club context is still retrieved from the teams table
        var clubId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT clubid FROM public.teams WHERE id = @teamId",
            new { teamId });

        var positionId = Guid.NewGuid();

        await conn.ExecuteAsync(@"
        INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
        VALUES (@positionId, @sportId, 'Forward', 'FW')
        ON CONFLICT (id) DO NOTHING",
            new { positionId, sportId });

        for (int i = 0; i < count; i++)
        {
            var playerId = Guid.NewGuid();
            var rosterId = Guid.NewGuid();
            ids.Add(rosterId);

            await conn.ExecuteAsync(@"
            INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) 
            VALUES (@playerId, @clubId, 'First', @last, '2000-01-01', 0, now())",
                new { playerId, clubId, last = Guid.NewGuid().ToString()[..8] });

            // Deterministic, collision-free jersey number across all test invocations.
            // Interlocked.Increment guarantees thread-safety and no duplicates for the
            // uix_playerrosters_tournament_team_number (tournamentId, teamId, number) constraint.
            var playerNumber = Interlocked.Increment(ref _playerNumberSeed);

            await conn.ExecuteAsync(@"
            INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat)
            VALUES (@rosterId, @playerId, @tournamentId, @teamId, @num, @positionId, now())",
                new { rosterId, playerId, tournamentId, teamId, positionId, num = playerNumber });
        }
        return ids;
    }

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

    /// <summary>
    /// Seeds a game event for a specific match lineup entry to test linked event detection.
    /// Updated to reflect the removal of matchid from the gameevents table.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="lineupId">The unique identifier of the match lineup entry.</param>
    private async Task SeedGameEventAsync(Guid matchId, Guid lineupId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // 1. Retrieve tournament and sport IDs to create a valid event definition
        var tournamentId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT tournamentid FROM public.matches WHERE id = @mid",
            new { mid = matchId });

        var sportId = await conn.ExecuteScalarAsync<Guid>(
            "SELECT sportid FROM public.tournaments WHERE id = @tid",
            new { tid = tournamentId });

        // 2. Insert a valid event definition if it doesn't exist
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

        // 3. Insert the game event without matchid column
        // Relationship is now strictly managed via matchlineupid
        await conn.ExecuteAsync(@"
        INSERT INTO public.gameevents (
            id, 
            matchlineupid, 
            eventdefinitionid, 
            periodnumber, 
            eventtimestamp, 
            isleadtogoal, 
            createdat
        ) 
        VALUES (@id, @lid, @edid, @period, @timestamp, @isLead, now())",
            new
            {
                id = Guid.NewGuid(),
                lid = lineupId,
                edid = eventDefId,
                period = 1,
                timestamp = DateTimeOffset.UtcNow,
                isLead = false
            });
    }

    /// <summary>
    /// Helper method to retrieve the total number of lineup entries for a specific match.
    /// </summary>
    /// <param name="matchId">The match identifier.</param>
    /// <returns>The count of records in public.matchlineups.</returns>
    private async Task<int> GetLineupCountAsync(Guid matchId)
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.matchlineups WHERE matchid = @mid",
            new { mid = matchId });
    }

    #endregion
}