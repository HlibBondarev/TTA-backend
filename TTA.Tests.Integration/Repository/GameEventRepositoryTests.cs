using Dapper;
using FluentAssertions;
using System.Data.Common;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for the <see cref="GameEventRepository"/>.
/// Validates data access logic and PostgreSQL storage function integration for batch operations.
/// </summary>
public class GameEventRepositoryTests : BaseIntegrationTest
{
    private readonly GameEventRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameEventRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture instance.</param>
    public GameEventRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new GameEventRepository(fixture.ConnectionFactory);
    }

    #region Integration Tests

    /// <summary>
    /// Verifies that <see cref="GameEventRepository.UpsertAsync"/> correctly persists a batch of new game events.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_ShouldPersistNewEvent_WhenDataIsValid()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();
        var gameEvent = CreateEventModel(context.LineupId, context.DefinitionId);

        // Act
        var result = await _repository.UpsertAsync([gameEvent]);

        // Assert
        result.Should().NotBeNull();
        var savedEvent = result.Should().ContainSingle().Subject;
        savedEvent.Id.Should().Be(gameEvent.Id);

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
        await _repository.UpsertAsync([gameEvent]);

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
        await _repository.UpsertAsync([newEvent]);
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

        await _repository.UpsertAsync([event1, event2]);

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
        await _repository.UpsertAsync([newEvent]);

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
    /// </summary>
    [Fact]
    public async Task NormalizeMatchEventsTimeAsync_ShouldBatchUpdateNormalizedMatchTime_BasedOnTimeAnchors()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync();

        var now = DateTime.UtcNow;
        var baseTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);

        var configId = Guid.Empty;
        var originalDuration = 0;

        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            var configData = await conn.QuerySingleAsync<(Guid Id, int Duration)>(@"
                SELECT id, perioddurationminutes 
                FROM public.sportconfigurations 
                WHERE id = (SELECT configurationid FROM public.tournaments WHERE id = (SELECT tournamentid FROM public.matches WHERE id = @mid));",
                new { mid = context.MatchId });

            configId = configData.Id;
            originalDuration = configData.Duration;

            await conn.ExecuteAsync(@"
                UPDATE public.sportconfigurations 
                SET perioddurationminutes = 8 
                WHERE id = @id;",
                new { id = configId });

            await conn.ExecuteAsync("DELETE FROM public.timeanchors WHERE matchid = @mid;", new { mid = context.MatchId });

            await conn.ExecuteAsync(@"
                INSERT INTO public.timeanchors (id, matchid, periodnumber, type, timestamp) VALUES 
                (@id1, @mid, 1, 0, @t0),
                (@id2, @mid, 1, 1, @t10);",
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
            var gameEvent = CreateEventModel(context.LineupId, context.DefinitionId);
            gameEvent.PeriodNumber = 1;
            gameEvent.EventTimestamp = baseTime.AddSeconds(300);
            gameEvent.NormalizedMatchTime = TimeSpan.Zero;

            await _repository.UpsertAsync([gameEvent]);

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

            var expectedNormalizedTime = TimeSpan.FromSeconds(240);
            updatedEvent!.NormalizedMatchTime.Should().NotBeNull();
            updatedEvent.NormalizedMatchTime!.Value.Should().BeCloseTo(expectedNormalizedTime, TimeSpan.FromMilliseconds(500));
        }
        finally
        {
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

    /// <summary>
    /// Verifies that <see cref="GameEventRepository.UpsertAsync"/> correctly persists and retrieves both general location 
    /// coordinates and shot coordinates when the event definition has <c>isshot = true</c>.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_ShouldPersistAndRetrieveShotCoordinates_WhenEventIsShot()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync(isShotDefinition: true);
        var gameEvent = CreateEventModel(context.LineupId, context.DefinitionId);
        gameEvent.LocationX = 4.50m;
        gameEvent.LocationY = 8.25m;
        gameEvent.ShotLocationX = 12.30m;
        gameEvent.ShotLocationY = 45.60m;

        // Act
        await _repository.UpsertAsync([gameEvent]);

        // Assert
        var persisted = await _repository.GetByIdAsync(gameEvent.Id);
        persisted.Should().NotBeNull();
        persisted!.LocationX.Should().Be(4.50m);
        persisted.LocationY.Should().Be(8.25m);
        persisted.ShotLocationX.Should().Be(12.30m, "shotlocationx must be saved when isshot = true");
        persisted.ShotLocationY.Should().Be(45.60m, "shotlocationy must be saved when isshot = true");
    }

    /// <summary>
    /// Verifies that <see cref="GameEventRepository.UpsertAsync"/> automatically masks shot coordinates
    /// (sets <c>shotlocationx</c> and <c>shotlocationy</c> to <c>null</c>) when the referenced event definition 
    /// has <c>isshot = false</c>, even if non-null shot coordinates were passed in the model.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_ShouldMaskShotCoordinatesToNull_WhenEventDefinitionIsNotShot()
    {
        // Arrange
        var context = await SeedEventEnvironmentAsync(isShotDefinition: false);
        var gameEvent = CreateEventModel(context.LineupId, context.DefinitionId);
        gameEvent.LocationX = 14.50m;
        gameEvent.LocationY = 28.25m;
        gameEvent.ShotLocationX = 99.99m; // Accidental client payload
        gameEvent.ShotLocationY = 88.88m;

        // Act
        await _repository.UpsertAsync([gameEvent]);

        // Assert
        var persisted = await _repository.GetByIdAsync(gameEvent.Id);
        persisted.Should().NotBeNull();
        persisted!.LocationX.Should().Be(14.50m, "general action location must be preserved");
        persisted.LocationY.Should().Be(28.25m, "general action location must be preserved");
        persisted.ShotLocationX.Should().BeNull("shotlocationx must be masked to NULL when isshot = false");
        persisted.ShotLocationY.Should().BeNull("shotlocationy must be masked to NULL when isshot = false");
    }

    #endregion

    #region Seed Helpers

    /// <summary>
    /// Seeds the environment for integration tests using safe SQL insertion patterns.
    /// </summary>
    /// <param name="isShotDefinition">Specifies whether the seeded event definition represents a shot action.</param>
    private async Task<(Guid MatchId, Guid LineupId, Guid DefinitionId, Guid SportId)> SeedEventEnvironmentAsync(bool isShotDefinition = true)
    {
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Geography
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) 
            SELECT 'Ukraine', 'UA' WHERE NOT EXISTS (SELECT 1 FROM public.countries WHERE name = 'Ukraine')",
            transaction: transaction);
        var countryId = await conn.QuerySingleAsync<int>("SELECT id FROM public.countries WHERE name = 'Ukraine'", transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.regions (countryid, name) 
            SELECT @cid, 'Dnipro Region' WHERE NOT EXISTS (SELECT 1 FROM public.regions WHERE name = 'Dnipro Region' AND countryid = @cid)",
            new { cid = countryId }, transaction: transaction);
        var regionId = await conn.QuerySingleAsync<int>("SELECT id FROM public.regions WHERE name = 'Dnipro Region'", transaction: transaction);

        var cityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await conn.ExecuteAsync(@"
            INSERT INTO public.cities (id, regionid, name) 
            SELECT @id, @rid, 'Dnipro' WHERE NOT EXISTS (SELECT 1 FROM public.cities WHERE id = @id)",
            new { id = cityId, rid = regionId }, transaction: transaction);

        // User
        var userId = "auth0|integration-tester";
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) 
            SELECT @id, 'test@tta.com', 'Tester', NOW() WHERE NOT EXISTS (SELECT 1 FROM public.users WHERE id = @id)",
            new { id = userId }, transaction: transaction);

        // Sport & SportConfiguration
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var sportName = $"Sport_{suffix}";
        var shortName = suffix[..3].ToUpper();

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @name, @shortName, @configId)",
            new { id = sportId, name = sportName, shortName, configId }, transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit, playground, fieldlength, fieldwidth) 
            VALUES (@id, @sid, false, 2, 45, '105x68', 25, 11, 7, 'Playground', 25, 20)",
            new { id = configId, sid = sportId }, transaction: transaction);

        // Transactional Data
        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityid, @name, NOW())",
            new { id = clubId, cityid = cityId, name = $"Club_{suffix}" }, transaction: transaction);

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sid, @configId, @cityid, @oid, @name, NOW(), NOW())",
            new { id = tournamentId, sid = sportId, configId, cityid = cityId, oid = userId, name = $"Tournament_{suffix}" }, transaction: transaction);

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cid, @sid, @name, 0, NOW())",
            new { id = teamId, cid = clubId, sid = sportId, name = $"Team_{suffix}" }, transaction: transaction);

        var playerId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES (@id, @cid, 'Test', @name, '2000-01-01', 0, NOW())",
            new { id = playerId, cid = clubId, name = $"Player_{suffix}" }, transaction: transaction);

        var posId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) 
            VALUES (@id, @sid, 'Forward', 'FW')",
            new { id = posId, sid = sportId }, transaction: transaction);

        var rosterId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat) 
            VALUES (@id, @pid, @tid, @teamid, 10, @posid, NOW())",
            new { id = rosterId, pid = playerId, tid = tournamentId, teamid = teamId, posid = posId }, transaction: transaction);

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, NOW(), NOW())",
            new { id = matchId, tid = tournamentId, teamid = teamId }, transaction: transaction);

        var lineupId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid) 
            VALUES (@id, @mid, @rid, 10, @posid)",
            new { id = lineupId, mid = matchId, rid = rosterId, posid = posId }, transaction: transaction);

        var defId = Guid.NewGuid();
        var defName = isShotDefinition ? "Goal" : "Exclusion";
        var defShortName = isShotDefinition ? "G" : "EX";

        await conn.ExecuteAsync(@"
            INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, isshot, createdat) 
            VALUES (@id, @sid, @name, @shortName, @isPositive, @isShot, NOW())",
            new { id = defId, sid = sportId, name = defName, shortName = defShortName, isPositive = isShotDefinition, isShot = isShotDefinition }, transaction: transaction);

        await transaction.CommitAsync();

        return (matchId, lineupId, defId, sportId);
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