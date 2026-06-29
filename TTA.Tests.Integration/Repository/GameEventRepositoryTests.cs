using Dapper;
using FluentAssertions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for the <see cref="GameEventRepository"/>.
/// Validates data access logic and PostgreSQL storage function integration.
/// </summary>
public class GameEventRepositoryTests : BaseIntegrationTest
{
    private readonly GameEventRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameEventRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture.</param>
    public GameEventRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new GameEventRepository(fixture.ConnectionFactory);
    }

    #region Integration Tests

    /// <summary>
    /// Verifies that <see cref="GameEventRepository.UpsertAsync"/> correctly persists a new game event.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_ShouldPersistNewEvent_WhenDataIsValid()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();
        var gameEvent = CreateEventModel(context.LineupId, context.DefinitionId);

        // Act
        var result = await _repository.UpsertAsync(gameEvent);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(gameEvent.Id);

        var persisted = await _repository.GetByIdAsync(gameEvent.Id);
        persisted.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that <see cref="GameEventRepository.GetByIdAsync"/> retrieves an existing game event.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity_WhenExists()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();
        var gameEvent = CreateEventModel(context.LineupId, context.DefinitionId);
        await _repository.UpsertAsync(gameEvent);

        // Act
        var result = await _repository.GetByIdAsync(gameEvent.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(gameEvent.Id);
    }

    /// <summary>
    /// Verifies that detailed game event data is returned correctly when the event exists.
    /// </summary>
    [Fact]
    public async Task GetByIdWithDetailsAsync_ShouldReturnDetailedData_WhenExists()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();
        var newEvent = CreateEventModel(context.LineupId, context.DefinitionId);

        // Act
        await _repository.UpsertAsync(newEvent);
        var result = await _repository.GetByIdWithDetailsAsync(newEvent.Id);

        // Assert
        Assert.Equal(newEvent.Id, result!.Id);
    }

    /// <summary>
    /// Verifies that <see cref="GameEventRepository.GetMatchEventsAsync"/> returns all events for a match timeline.
    /// </summary>
    [Fact]
    public async Task GetMatchEventsAsync_ShouldReturnTimeline_ForSpecificMatch()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();
        var event1 = CreateEventModel(context.LineupId, context.DefinitionId);
        var event2 = CreateEventModel(context.LineupId, context.DefinitionId);

        await _repository.UpsertAsync(event1);
        await _repository.UpsertAsync(event2);

        // Act
        var timeline = await _repository.GetMatchEventsAsync(context.MatchId);

        // Assert
        var events = timeline.ToList();
        events.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies that an event is successfully removed from the database.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ShouldRemoveEvent_WhenExists()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();
        var newEvent = CreateEventModel(context.LineupId, context.DefinitionId);

        // Ensure record exists before deletion
        await _repository.UpsertAsync(newEvent);

        // Act
        var isDeleted = await _repository.DeleteAsync(newEvent.Id);
        var deletedCheck = await _repository.GetByIdAsync(newEvent.Id);

        // Assert
        isDeleted.Should().BeTrue("Repository must return true when the record is successfully deleted");
        deletedCheck.Should().BeNull("The record should no longer exist in the database");
    }

    /// <summary>
    /// Verifies that <see cref="GameEventRepository.NormalizeMatchEventsTimeAsync"/> successfully invokes 
    /// the database storage function to batch update event timestamps using piecewise-linear scale coefficients.
    /// Scenario setup:
    /// - Nominal period duration: 8 minutes.
    /// - Play time anchors setup: PeriodStart (0s real) -> PeriodEnd (600s real, i.e., 10 mins real play).
    /// - Scale factor K calculation: 8 minutes * 60 seconds / 600 seconds = 0.8
    /// - Game Event added at 300s real-world timestamp.
    /// - Expected normalized match time: 300 seconds * 0.8 = 240 seconds (4 minutes).
    /// </summary>
    [Fact]
    public async Task NormalizeMatchEventsTimeAsync_ShouldBatchUpdateNormalizedMatchTime_BasedOnTimeAnchors()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();
        var baseTime = DateTime.UtcNow;
        var configId = Guid.Empty;
        var originalDuration = 0;

        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            // Capture the shared configuration ID and its original duration to prevent state pollution
            var configData = await conn.QuerySingleAsync<(Guid Id, int Duration)>(@"
                SELECT id, perioddurationminutes 
                FROM public.sportconfigurations 
                WHERE id = (SELECT configurationid FROM public.tournaments WHERE id = (SELECT tournamentid FROM public.matches WHERE id = @mid));",
                new { mid = context.MatchId });

            configId = configData.Id;
            originalDuration = configData.Duration;

            // 1. Force sport configuration override to 8 minutes nominal duration (standard Water Polo configuration context)
            await conn.ExecuteAsync(@"
                UPDATE public.sportconfigurations 
                SET perioddurationminutes = 8 
                WHERE id = @id;",
                new { id = configId });

            // 2. Clear out any existing default seeded anchors to ensure strict timeline boundaries
            await conn.ExecuteAsync("DELETE FROM public.timeanchors WHERE matchid = @mid;", new { mid = context.MatchId });

            // 3. Seed deterministic timeline play anchors without non-existent 'createdat' column
            await conn.ExecuteAsync(@"
                INSERT INTO public.timeanchors (id, matchid, periodnumber, type, timestamp) VALUES 
                (@id1, @mid, 1, 0, @t0), -- PeriodStart at 0 seconds elapsed
                (@id2, @mid, 1, 1, @t10); -- PeriodEnd at 600 seconds elapsed",
                new
                {
                    id1 = Guid.NewGuid(),
                    id2 = Guid.NewGuid(),
                    mid = context.MatchId,
                    t0 = baseTime,
                    t10 = baseTime.AddSeconds(600)
                });
        }

        try
        {
            // 4. Instantiate a new event occurring precisely mid-way through the real-world execution window (300 seconds)
            var gameEvent = CreateEventModel(context.LineupId, context.DefinitionId);
            gameEvent.PeriodNumber = 1;
            gameEvent.EventTimestamp = baseTime.AddSeconds(300);
            gameEvent.NormalizedMatchTime = TimeSpan.Zero; // Reset to verify the database operation updates this explicitly

            await _repository.UpsertAsync(gameEvent);

            // Fetch team ID associated with this match context boundary
            Guid teamId;
            using (var conn = Fixture.ConnectionFactory.CreateConnection())
            {
                teamId = await conn.QuerySingleAsync<Guid>(@"
                    SELECT pr.teamid 
                    FROM public.matchlineups ml
                    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
                    WHERE ml.id = @lid;", new { lid = context.LineupId });
            }

            // Act
            await _repository.NormalizeMatchEventsTimeAsync(context.MatchId, teamId);

            // Assert
            var updatedEvent = await _repository.GetByIdAsync(gameEvent.Id);
            updatedEvent.Should().NotBeNull();

            // K = (8 * 60) / 600 = 0.8
            // Event elapsed seconds = 300. Normalized time = 300 * 0.8 = 240 seconds.
            var expectedNormalizedTime = TimeSpan.FromSeconds(240);
            updatedEvent!.NormalizedMatchTime.Should().Be(expectedNormalizedTime);
        }
        finally
        {
            // Revert the shared sport configuration row back to its original seed state to maintain test isolation
            if (configId != Guid.Empty)
            {
                using (var conn = Fixture.ConnectionFactory.CreateConnection())
                {
                    await conn.ExecuteAsync(@"
                        UPDATE public.sportconfigurations 
                        SET perioddurationminutes = @Minutes 
                        WHERE id = @Id;",
                        new { Minutes = originalDuration, Id = configId });
                }
            }
        }
    }

    #endregion

    #region Seed Helpers

    /// <summary>
    /// Seeds the environment for integration tests using safe SQL insertion patterns.
    /// </summary>
    private async Task<(Guid MatchId, Guid LineupId, Guid DefinitionId)> SeedEventEnvironmentAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // Geography
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) 
            SELECT 'Ukraine', 'UA' WHERE NOT EXISTS (SELECT 1 FROM public.countries WHERE name = 'Ukraine')");
        var countryId = await conn.QuerySingleAsync<int>("SELECT id FROM public.countries WHERE name = 'Ukraine'");

        await conn.ExecuteAsync(@"
            INSERT INTO public.regions (countryid, name) 
            SELECT @cid, 'Dnipro Region' WHERE NOT EXISTS (SELECT 1 FROM public.regions WHERE name = 'Dnipro Region' AND countryid = @cid)",
            new { cid = countryId });
        var regionId = await conn.QuerySingleAsync<int>("SELECT id FROM public.regions WHERE name = 'Dnipro Region'");

        var cityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await conn.ExecuteAsync(@"
            INSERT INTO public.cities (id, regionid, name) 
            SELECT @id, @rid, 'Dnipro' WHERE NOT EXISTS (SELECT 1 FROM public.cities WHERE id = @id)",
            new { id = cityId, rid = regionId });

        // User
        var userId = "auth0|integration-tester";
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) 
            SELECT @id, 'test@tta.com', 'Tester', NOW() WHERE NOT EXISTS (SELECT 1 FROM public.users WHERE id = @id)",
            new { id = userId });

        // Sport
        var sportId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name) 
            SELECT @id, 'Football' WHERE NOT EXISTS (SELECT 1 FROM public.sports WHERE id = @id)",
            new { id = sportId });

        var configId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            SELECT @id, @sid, false, 2, 45, '105x68', 25, 11 WHERE NOT EXISTS (SELECT 1 FROM public.sportconfigurations WHERE sportid = @sid)",
            new { id = configId, sid = sportId });

        // Transactional Data
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityid, @name, NOW())",
            new { id = clubId, cityid = cityId, name = $"Club_{suffix}" });

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sid, (SELECT id FROM public.sportconfigurations WHERE sportid = @sid LIMIT 1), @cityid, @oid, @name, NOW(), NOW())",
            new { id = tournamentId, sid = sportId, cityid = cityId, oid = userId, name = $"Tournament_{suffix}" });

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cid, @sid, @name, 0, NOW())",
            new { id = teamId, cid = clubId, sid = sportId, name = $"Team_{suffix}" });

        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'Test', @name, '2000-01-01', 0, NOW())",
            new { id = playerId, cid = clubId, name = $"Player_{suffix}" });

        var posId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            SELECT @id, @sid, 'Forward', 'FW' WHERE NOT EXISTS (SELECT 1 FROM public.playerpositiondefinitions WHERE id = @id)",
            new { id = posId, sid = sportId });

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) 
            VALUES (@id, @pid, @tid, @teamid, 10, @posid, NOW())",
            new { id = rosterId, pid = playerId, tid = tournamentId, teamid = teamId, posid = posId });

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, NOW(), NOW())",
            new { id = matchId, tid = tournamentId, teamid = teamId });

        var lineupId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, isinstartinglineup, positionid) 
            VALUES (@id, @mid, @rid, 10, true, @posid)",
            new { id = lineupId, mid = matchId, rid = rosterId, posid = posId });

        var defId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        await conn.ExecuteAsync(@"
            INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat) 
            SELECT @id, @sid, 'Goal', 'G', true, NOW() WHERE NOT EXISTS (SELECT 1 FROM public.eventdefinitions WHERE id = @id)",
            new { id = defId, sid = sportId });

        return (matchId, lineupId, defId);
    }

    /// <summary>
    /// Creates a standard <see cref="GameEvent"/> model for testing.
    /// </summary>
    private static GameEvent CreateEventModel(Guid lineupId, Guid definitionId)
    {
        return new GameEvent
        {
            Id = Guid.NewGuid(),
            MatchLineupId = lineupId,
            EventDefinitionId = definitionId,
            PeriodNumber = 1,
            EventTimestamp = DateTime.UtcNow,
            NormalizedMatchTime = TimeSpan.FromMinutes(15),
            IsLeadToGoal = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    #endregion
}