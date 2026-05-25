using Dapper;
using FluentAssertions;
using Npgsql;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for the <see cref="PlayerPresenceRepository"/>.
/// Validates data access logic, database constraints, and PostgreSQL storage function execution for player presence tracking.
/// </summary>
public class PlayerPresenceRepositoryTests : BaseIntegrationTest
{
    private readonly PlayerPresenceRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerPresenceRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture managing containerized PostgreSQL lifecycles.</param>
    public PlayerPresenceRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new PlayerPresenceRepository(fixture.ConnectionFactory);
    }

    #region Integration Tests

    /// <summary>
    /// Verifies that <see cref="PlayerPresenceRepository.RecordPresenceAsync"/> correctly persists a new presence record (TimeIn).
    /// </summary>
    [Fact]
    public async Task RecordPresenceAsync_ShouldInsertNewPresence_WhenDataIsValid()
    {
        // Arrange
        var context = await SeedPresenceEnvironmentAsync();
        var exactTimeIn = DateTime.UtcNow;

        var presence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = context.LineupId1,
            PeriodNumber = 1,
            TimeIn = exactTimeIn,
            TimeOut = null
        };

        // Act
        var resultId = await _repository.RecordPresenceAsync(presence);

        // Assert
        resultId.Should().Be(presence.Id);

        var matchPresences = await _repository.GetMatchPresenceAsync(context.MatchId);
        var persisted = matchPresences.FirstOrDefault(p => p.Id == presence.Id);

        persisted.Should().NotBeNull();
        persisted!.MatchLineupId.Should().Be(context.LineupId1);
        persisted.PeriodNumber.Should().Be(1);
        persisted.TimeOut.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="PlayerPresenceRepository.RecordPresenceAsync"/> successfully updates an existing record (e.g., setting TimeOut).
    /// </summary>
    [Fact]
    public async Task RecordPresenceAsync_ShouldUpdateExistingPresence_WhenRecordExists()
    {
        // Arrange
        var context = await SeedPresenceEnvironmentAsync();
        var exactTimeIn = DateTime.UtcNow;
        var exactTimeOut = exactTimeIn.AddMinutes(15);

        var presence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = context.LineupId1,
            PeriodNumber = 1,
            TimeIn = exactTimeIn,
            TimeOut = null
        };

        await _repository.RecordPresenceAsync(presence); // Insert

        // Act - Set TimeOut and trigger Upsert logic
        presence.TimeOut = exactTimeOut;
        await _repository.RecordPresenceAsync(presence); // Update

        // Assert
        var matchPresences = await _repository.GetMatchPresenceAsync(context.MatchId);
        var updated = matchPresences.FirstOrDefault(p => p.Id == presence.Id);

        updated.Should().NotBeNull();
        updated!.TimeOut.Should().BeCloseTo(exactTimeOut, TimeSpan.FromMilliseconds(100)); // DB precision check
    }

    /// <summary>
    /// Verifies that <see cref="PlayerPresenceRepository.RecordSubstitutionAsync"/> atomically updates the outgoing player and inserts the incoming player.
    /// </summary>
    [Fact]
    public async Task RecordSubstitutionAsync_ShouldUpdateOutgoingAndInsertIncoming_Atomically_WhenDataIsValid()
    {
        // Arrange
        var context = await SeedPresenceEnvironmentAsync();
        var baseTime = DateTime.UtcNow;
        var substitutionTime = baseTime.AddMinutes(15);

        // Record initial active presence for the outgoing player
        var outgoingPresence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = context.LineupId1,
            PeriodNumber = 1,
            TimeIn = baseTime,
            TimeOut = null
        };
        await _repository.RecordPresenceAsync(outgoingPresence);

        // Prepare the mutation state for substitution
        outgoingPresence.TimeOut = substitutionTime;

        var incomingPresence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = context.LineupId2,
            PeriodNumber = 1,
            TimeIn = substitutionTime,
            TimeOut = null
        };

        // Act
        var resultId = await _repository.RecordSubstitutionAsync(outgoingPresence, incomingPresence);

        // Assert
        resultId.Should().Be(incomingPresence.Id);

        var matchPresences = (await _repository.GetMatchPresenceAsync(context.MatchId)).ToList();

        var persistedOutgoing = matchPresences.FirstOrDefault(p => p.Id == outgoingPresence.Id);
        persistedOutgoing.Should().NotBeNull();
        persistedOutgoing!.TimeOut.Should().BeCloseTo(substitutionTime, TimeSpan.FromMilliseconds(100));

        var persistedIncoming = matchPresences.FirstOrDefault(p => p.Id == incomingPresence.Id);
        persistedIncoming.Should().NotBeNull();
        persistedIncoming!.TimeIn.Should().BeCloseTo(substitutionTime, TimeSpan.FromMilliseconds(100));
        persistedIncoming.TimeOut.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="PlayerPresenceRepository.RecordSubstitutionAsync"/> completely rolls back the transaction if a database constraint fails.
    /// </summary>
    [Fact]
    public async Task RecordSubstitutionAsync_ShouldRollbackTransaction_WhenDatabaseThrowsException()
    {
        // Arrange
        var context = await SeedPresenceEnvironmentAsync();
        var baseTime = DateTime.UtcNow;
        var substitutionTime = baseTime.AddMinutes(15);

        // Record initial active presence for the outgoing player
        var outgoingPresence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = context.LineupId1,
            PeriodNumber = 1,
            TimeIn = baseTime,
            TimeOut = null
        };
        await _repository.RecordPresenceAsync(outgoingPresence);

        // Prepare INVALID mutation state to force a database CHECK constraint violation
        // (TimeOut is set to be BEFORE TimeIn, which violates 'chk_timeout_after_timein' constraint)
        outgoingPresence.TimeOut = baseTime.AddMinutes(-5);

        var incomingPresence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = context.LineupId2,
            PeriodNumber = 1,
            TimeIn = substitutionTime,
            TimeOut = null
        };

        // Act
        var act = async () => await _repository.RecordSubstitutionAsync(outgoingPresence, incomingPresence);

        // Assert
        await act.Should().ThrowAsync<PostgresException>(); // The DB constraint violation triggers a PostgresException

        // Verify Rollback: Outgoing should NOT have its TimeOut updated, Incoming should NOT be inserted
        var matchPresences = (await _repository.GetMatchPresenceAsync(context.MatchId)).ToList();

        var persistedOutgoing = matchPresences.FirstOrDefault(p => p.Id == outgoingPresence.Id);
        persistedOutgoing.Should().NotBeNull();
        persistedOutgoing!.TimeOut.Should().BeNull(); // Confirms rollback: the session remains active

        var persistedIncoming = matchPresences.FirstOrDefault(p => p.Id == incomingPresence.Id);
        persistedIncoming.Should().BeNull(); // Confirms rollback: the incoming player was never inserted
    }

    /// <summary>
    /// Verifies that <see cref="PlayerPresenceRepository.GetMatchPresenceAsync"/> retrieves chronological history of a match.
    /// </summary>
    [Fact]
    public async Task GetMatchPresenceAsync_ShouldReturnChronologicalTimeline_ForMatch()
    {
        // Arrange
        var context = await SeedPresenceEnvironmentAsync();
        var baseTime = DateTime.UtcNow;

        // Player 1 plays Period 1
        await _repository.RecordPresenceAsync(new PlayerPresence { Id = Guid.NewGuid(), MatchLineupId = context.LineupId1, PeriodNumber = 1, TimeIn = baseTime });

        // Player 2 plays Period 2 (Should appear later in the list)
        await _repository.RecordPresenceAsync(new PlayerPresence { Id = Guid.NewGuid(), MatchLineupId = context.LineupId2, PeriodNumber = 2, TimeIn = baseTime.AddMinutes(20) });

        // Player 1 substitutes in during Period 2
        await _repository.RecordPresenceAsync(new PlayerPresence { Id = Guid.NewGuid(), MatchLineupId = context.LineupId1, PeriodNumber = 2, TimeIn = baseTime.AddMinutes(25) });

        // Act
        var timeline = (await _repository.GetMatchPresenceAsync(context.MatchId)).ToList();

        // Assert
        timeline.Count.Should().Be(3);

        // Verify correct chronological ordering (by PeriodNumber ASC, TimeIn ASC as defined in storage function)
        timeline[0].PeriodNumber.Should().Be(1);
        timeline[0].MatchLineupId.Should().Be(context.LineupId1);

        timeline[1].PeriodNumber.Should().Be(2);
        timeline[1].MatchLineupId.Should().Be(context.LineupId2);

        timeline[2].PeriodNumber.Should().Be(2);
        timeline[2].MatchLineupId.Should().Be(context.LineupId1);
    }

    /// <summary>
    /// Verifies that <see cref="PlayerPresenceRepository.InitializePeriodPresenceAsync"/> executes a bulk insert for multiple lineup IDs.
    /// </summary>
    [Fact]
    public async Task InitializePeriodPresenceAsync_ShouldBulkInsert_ForProvidedLineupIds()
    {
        // Arrange
        var context = await SeedPresenceEnvironmentAsync();
        var exactTimeIn = DateTime.UtcNow;
        var lineupIds = new List<Guid> { context.LineupId1, context.LineupId2 };
        var precision = TimeSpan.FromMilliseconds(500);

        // Act
        await _repository.InitializePeriodPresenceAsync(
            periodNumber: 1,
            timeIn: exactTimeIn,
            lineupIds: lineupIds);

        // Assert
        var matchPresences = (await _repository.GetMatchPresenceAsync(context.MatchId)).ToList();

        matchPresences.Count.Should().Be(2);

        // Verify Player 1 presence
        var presence1 = matchPresences.FirstOrDefault(p => p.MatchLineupId == context.LineupId1);
        presence1.Should().NotBeNull();
        presence1!.TimeIn.Should().BeCloseTo(exactTimeIn, precision);
        presence1.TimeOut.Should().BeNull();
        presence1.PeriodNumber.Should().Be(1);

        // Verify Player 2 presence
        var presence2 = matchPresences.FirstOrDefault(p => p.MatchLineupId == context.LineupId2);
        presence2.Should().NotBeNull();
        presence2!.TimeIn.Should().BeCloseTo(exactTimeIn, precision);
        presence2.TimeOut.Should().BeNull();
        presence2.PeriodNumber.Should().Be(1);
    }

    /// <summary>
    /// Verifies that <see cref="PlayerPresenceRepository.CloseActivePresencesAsync"/> correctly identifies and updates open sessions for a specific period.
    /// </summary>
    [Fact]
    public async Task CloseActivePresencesAsync_ShouldSetTimeout_ForOpenSessionsInPeriod()
    {
        // Arrange
        var context = await SeedPresenceEnvironmentAsync();
        var exactTimeIn = DateTime.UtcNow;
        var exactTimeOut = exactTimeIn.AddMinutes(15);
        var lineupIds = new List<Guid> { context.LineupId1, context.LineupId2 };

        // Bulk insert two players with open sessions for Period 1
        await _repository.InitializePeriodPresenceAsync(1, exactTimeIn, lineupIds);

        // Insert a third record that is already CLOSED (TimeOut is not null) - should not be modified
        var closedPresenceId = Guid.NewGuid();
        await _repository.RecordPresenceAsync(new PlayerPresence
        {
            Id = closedPresenceId,
            MatchLineupId = context.LineupId1,
            PeriodNumber = 1,
            TimeIn = exactTimeIn.AddMinutes(-20),
            TimeOut = exactTimeIn.AddMinutes(-5)
        });

        // Act
        await _repository.CloseActivePresencesAsync(context.MatchId, 1, exactTimeOut);

        // Assert
        var matchPresences = (await _repository.GetMatchPresenceAsync(context.MatchId)).ToList();

        var closedManually = matchPresences.First(p => p.Id == closedPresenceId);
        closedManually.TimeOut.Should().NotBe(exactTimeOut); // Ensure it wasn't accidentally overwritten

        var newlyClosedSessions = matchPresences.Where(p => p.Id != closedPresenceId).ToList();
        newlyClosedSessions.Count.Should().Be(2);
        newlyClosedSessions.Should().OnlyContain(p => p.TimeOut.HasValue && p.TimeOut.Value.ToString("yyyy-MM-dd HH:mm:ss") == exactTimeOut.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    #endregion

    #region Seed Helpers

    /// <summary>
    /// Seeds the environment for player presence integration tests.
    /// Uses robust ON CONFLICT DO NOTHING checks to avoid unique constraint violations
    /// regardless of test execution order or leftover static data.
    /// </summary>
    /// <returns>A tuple containing the generated MatchId and two unique MatchLineupIds.</returns>
    private async Task<(Guid MatchId, Guid LineupId1, Guid LineupId2)> SeedPresenceEnvironmentAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // 1. Geography (Safe unique constraint handling)
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) 
            VALUES ('Ukraine', 'UA') 
            ON CONFLICT (name) DO NOTHING");
        var countryId = await conn.QuerySingleAsync<int>("SELECT id FROM public.countries WHERE name = 'Ukraine'");

        await conn.ExecuteAsync(@"
            INSERT INTO public.regions (countryid, name) 
            VALUES (@cid, 'Dnipro Region') 
            ON CONFLICT (countryid, name) DO NOTHING",
            new { cid = countryId });
        var regionId = await conn.QuerySingleAsync<int>("SELECT id FROM public.regions WHERE name = 'Dnipro Region' AND countryid = @cid", new { cid = countryId });

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.cities (id, regionid, name) 
            VALUES (@id, @rid, 'Dnipro') 
            ON CONFLICT (regionid, name) DO NOTHING",
            new { id = cityId, rid = regionId });
        cityId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.cities WHERE regionid = @rid AND name = 'Dnipro'", new { rid = regionId });

        // 2. User & Sport (Safe unique constraint handling)
        var userId = "auth0|presence-tester";
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) 
            VALUES (@id, 'tester@tta.com', 'Tester', NOW()) 
            ON CONFLICT (id) DO NOTHING",
            new { id = userId });

        var sportId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name) 
            VALUES (@id, 'Water Polo') 
            ON CONFLICT (name) DO NOTHING",
            new { id = sportId });
        sportId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.sports WHERE name = 'Water Polo'");

        var configId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            SELECT @id, @sid, true, 4, 8, '30x20', 15, 7 
            WHERE NOT EXISTS (SELECT 1 FROM public.sportconfigurations WHERE sportid = @sid)",
            new { id = configId, sid = sportId });
        configId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.sportconfigurations WHERE sportid = @sid LIMIT 1", new { sid = sportId });

        var posId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            SELECT @id, @sid, 'Center Forward', 'CF' 
            WHERE NOT EXISTS (SELECT 1 FROM public.playerpositiondefinitions WHERE sportid = @sid AND shortname = 'CF')",
            new { id = posId, sid = sportId });
        posId = await conn.QuerySingleAsync<Guid>("SELECT id FROM public.playerpositiondefinitions WHERE sportid = @sid AND shortname = 'CF' LIMIT 1", new { sid = sportId });

        // 3. Organization (Club, Tournament, Team)
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityid, @name, NOW())",
            new { id = clubId, cityid = cityId, name = $"WP_Club_{Guid.NewGuid():N}" });

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sid, @cfgid, @cityid, @oid, @name, NOW(), NOW())",
            new { id = tournamentId, sid = sportId, cfgid = configId, cityid = cityId, oid = userId, name = $"WP_Cup_{Guid.NewGuid():N}" });

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cid, @sid, @name, 0, NOW())",
            new { id = teamId, cid = clubId, sid = sportId, name = $"WP_Team_{Guid.NewGuid():N}" });

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, NOW(), NOW())",
            new { id = matchId, tid = tournamentId, teamid = teamId });

        // 4. Players & Lineups
        var playerId1 = Guid.NewGuid();
        var playerId2 = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'P1', 'L1', '2000-01-01', 0, NOW())", new { id = playerId1, cid = clubId });
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'P2', 'L2', '2000-01-02', 0, NOW())", new { id = playerId2, cid = clubId });

        var rosterId1 = Guid.NewGuid();
        var rosterId2 = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) VALUES (@id, @pid, @tid, @teamid, 1, @posid, NOW())", new { id = rosterId1, pid = playerId1, tid = tournamentId, teamid = teamId, posid = posId });
        await conn.ExecuteAsync(@"INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) VALUES (@id, @pid, @tid, @teamid, 2, @posid, NOW())", new { id = rosterId2, pid = playerId2, tid = tournamentId, teamid = teamId, posid = posId });

        var lineupId1 = Guid.NewGuid();
        var lineupId2 = Guid.NewGuid();
        await conn.ExecuteAsync(@"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, isinstartinglineup, positionid) VALUES (@id, @mid, @rid, 1, true, @posid)", new { id = lineupId1, mid = matchId, rid = rosterId1, posid = posId });
        await conn.ExecuteAsync(@"INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, isinstartinglineup, positionid) VALUES (@id, @mid, @rid, 2, true, @posid)", new { id = lineupId2, mid = matchId, rid = rosterId2, posid = posId });

        return (matchId, lineupId1, lineupId2);
    }

    #endregion
}