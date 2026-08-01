using Dapper;
using FluentAssertions;
using System.Data.Common;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for the <see cref="EventDefinitionRepository"/>.
/// Validates data access logic and PostgreSQL storage function integration for retrieving match event definitions.
/// </summary>
public class EventDefinitionRepositoryTests : BaseIntegrationTest
{
    private readonly EventDefinitionRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventDefinitionRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture.</param>
    public EventDefinitionRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new EventDefinitionRepository(fixture.ConnectionFactory);
    }

    #region Integration Tests

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetMatchEventDefinitionsAsync"/> returns 
    /// all event definitions associated with the sport of the specified match.
    /// </summary>
    [Fact]
    public async Task GetMatchEventDefinitionsAsync_ShouldReturnEventDefinitions_WhenMatchAndDefinitionsExist()
    {
        // Arrange
        var (matchId, sportId, definitionIds) = await SeedEventDefinitionEnvironmentAsync(createDefinitions: true);

        // Act
        var definitions = await _repository.GetMatchEventDefinitionsAsync(matchId);

        // Assert
        var result = definitions.ToList();
        result.Should().NotBeNull();
        result.Should().HaveCount(definitionIds.Count);
        result.Should().OnlyContain(d => d.SportId == sportId);
        result.Select(d => d.Id).Should().BeEquivalentTo(definitionIds);
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetMatchEventDefinitionsAsync"/> returns 
    /// an empty collection when no event definitions exist for the match's sport.
    /// </summary>
    [Fact]
    public async Task GetMatchEventDefinitionsAsync_ShouldReturnEmpty_WhenNoDefinitionsExistForSport()
    {
        // Arrange
        var (matchId, _, _) = await SeedEventDefinitionEnvironmentAsync(createDefinitions: false);

        // Act
        var definitions = await _repository.GetMatchEventDefinitionsAsync(matchId);

        // Assert
        definitions.Should().NotBeNull();
        definitions.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetMatchEventDefinitionsAsync"/> returns 
    /// an empty collection when the specified match does not exist in the database.
    /// </summary>
    [Fact]
    public async Task GetMatchEventDefinitionsAsync_ShouldReturnEmpty_WhenMatchDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();

        // Act
        var definitions = await _repository.GetMatchEventDefinitionsAsync(nonExistentMatchId);

        // Assert
        definitions.Should().NotBeNull();
        definitions.Should().BeEmpty();
    }

    #endregion

    #region Seed Helpers

    private async Task<(Guid MatchId, Guid SportId, List<Guid> DefinitionIds)> SeedEventDefinitionEnvironmentAsync(bool createDefinitions)
    {
        // Cast IDbConnection to DbConnection to support asynchronous transactions
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        // 1. Geography Setup
        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) 
            SELECT 'Ukraine', 'UA' WHERE NOT EXISTS (SELECT 1 FROM public.countries WHERE name = 'Ukraine')",
            transaction: transaction);

        var countryId = await conn.QuerySingleAsync<int>(
            "SELECT id FROM public.countries WHERE name = 'Ukraine'",
            transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.regions (countryid, name) 
            SELECT @cid, 'Dnipro Region' WHERE NOT EXISTS (SELECT 1 FROM public.regions WHERE name = 'Dnipro Region' AND countryid = @cid)",
            new { cid = countryId },
            transaction: transaction);

        var regionId = await conn.QuerySingleAsync<int>(
            "SELECT id FROM public.regions WHERE name = 'Dnipro Region'",
            transaction: transaction);

        var cityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await conn.ExecuteAsync(@"
            INSERT INTO public.cities (id, regionid, name) 
            SELECT @id, @rid, 'Dnipro' WHERE NOT EXISTS (SELECT 1 FROM public.cities WHERE id = @id)",
            new { id = cityId, rid = regionId },
            transaction: transaction);

        // 2. User Setup
        var userId = "auth0|integration-tester";
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) 
            SELECT @id, 'test@tta.com', 'Tester', NOW() WHERE NOT EXISTS (SELECT 1 FROM public.users WHERE id = @id)",
            new { id = userId },
            transaction: transaction);

        // 3. Sport & SportConfiguration Setup
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var shortName = $"S_{sportId:N}"[..10];

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @name, @shortname, @configId)",
            new { id = sportId, name = $"Sport_{sportId:N}", shortname = shortName, configId },
            transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @sid, false, 2, 45, '105x68', 25, 11)",
            new { id = configId, sid = sportId },
            transaction: transaction);

        // 4. Event Definitions Setup
        var definitionIds = new List<Guid>();

        if (createDefinitions)
        {
            var def1 = Guid.NewGuid();
            var def2 = Guid.NewGuid();

            await conn.ExecuteAsync(@"
                INSERT INTO public.eventdefinitions (id, sportid, name, shortname, ispositive, createdat)
                VALUES 
                (@id1, @sid, 'Goal', 'G', true, NOW()),
                (@id2, @sid, 'Foul', 'F', false, NOW())",
                new { id1 = def1, id2 = def2, sid = sportId },
                transaction: transaction);

            definitionIds.Add(def1);
            definitionIds.Add(def2);
        }

        // 5. Transactional Entities Setup (Club, Tournament, Team, Match)
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@id, @cityid, @name, NOW())",
            new { id = clubId, cityid = cityId, name = $"Club_{suffix}" },
            transaction: transaction);

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat) 
            VALUES (@id, @sid, @configId, @cityid, @oid, @name, NOW(), NOW())",
            new { id = tournamentId, sid = sportId, configId, cityid = cityId, oid = userId, name = $"Tournament_{suffix}" },
            transaction: transaction);

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync("INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@id, @cid, @sid, @name, 0, NOW())",
            new { id = teamId, cid = clubId, sid = sportId, name = $"Team_{suffix}" },
            transaction: transaction);

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, NOW(), NOW())",
            new { id = matchId, tid = tournamentId, teamid = teamId },
            transaction: transaction);

        await transaction.CommitAsync();

        return (matchId, sportId, definitionIds);
    }

    #endregion
}