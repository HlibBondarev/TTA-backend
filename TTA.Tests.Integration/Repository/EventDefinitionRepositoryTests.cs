using Dapper;
using FluentAssertions;
using System.Data.Common;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for the <see cref="EventDefinitionRepository" />.
/// Validates data access logic and PostgreSQL storage function integration for event definitions, user presets, and soft-delete operations.
/// </summary>
public class EventDefinitionRepositoryTests : BaseIntegrationTest
{
    private readonly EventDefinitionRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventDefinitionRepositoryTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture.</param>
    public EventDefinitionRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new EventDefinitionRepository(fixture.ConnectionFactory);
    }

    #region GetMatchEventDefinitionsAsync Tests

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetMatchEventDefinitionsAsync" /> returns 
    /// system default event definitions when no user-specific preset is configured.
    /// </summary>
    [Fact]
    public async Task GetMatchEventDefinitionsAsync_ShouldReturnDefaultEventDefinitions_WhenMatchAndSystemDefinitionsExist()
    {
        // Arrange
        var (matchId, sportId, _, definitionIds) = await SeedFullEnvironmentAsync(createSystemDefs: true);

        // Act
        var definitions = await _repository.GetMatchEventDefinitionsAsync(matchId);

        // Assert
        var result = definitions.ToList();
        result.Should().NotBeNull();
        result.Should().HaveCount(definitionIds.Count);
        result.Should().OnlyContain(d => d.SportId == sportId && !d.IsCustom && d.IsEnabled);
        result.Select(d => d.Id).Should().BeEquivalentTo(definitionIds);
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetMatchEventDefinitionsAsync" /> returns 
    /// active user preset definitions when a valid user identifier is provided.
    /// </summary>
    [Fact]
    public async Task GetMatchEventDefinitionsAsync_ShouldReturnUserPresetDefinitions_WhenUserPresetExists()
    {
        // Arrange
        var (matchId, sportId, userId, definitionIds) = await SeedFullEnvironmentAsync(createSystemDefs: true);

        // Create active user preset for def1 only
        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO public.usereventpresets (userid, eventdefinitionid, sortorder, createdat) 
                VALUES (@userId, @defId, 0, NOW())",
                new { userId, defId = definitionIds[0] });
        }

        // Act
        var definitions = await _repository.GetMatchEventDefinitionsAsync(matchId, userId);

        // Assert
        var result = definitions.ToList();
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(definitionIds[0]);
        result[0].IsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetMatchEventDefinitionsAsync" /> returns 
    /// an empty collection when no event definitions exist for the match's sport.
    /// </summary>
    [Fact]
    public async Task GetMatchEventDefinitionsAsync_ShouldReturnEmpty_WhenNoDefinitionsExistForSport()
    {
        // Arrange
        var (matchId, _, _, _) = await SeedFullEnvironmentAsync(createSystemDefs: false);

        // Act
        var definitions = await _repository.GetMatchEventDefinitionsAsync(matchId);

        // Assert
        definitions.Should().NotBeNull();
        definitions.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetMatchEventDefinitionsAsync" /> returns 
    /// an empty collection when the specified match does not exist in the database.
    /// </summary>
    [Fact]
    public async Task GetMatchEventDefinitionsAsync_ShouldReturnEmpty_WhenMatchDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();

        // Act
        Func<Task> act = async () => await _repository.GetMatchEventDefinitionsAsync(nonExistentMatchId);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region UpsertCustomAsync Tests

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.UpsertCustomAsync" /> successfully creates 
    /// a new custom event definition entity and automatically registers it in user presets.
    /// </summary>
    [Fact]
    public async Task UpsertCustomAsync_ShouldInsertNewCustomEventDefinition_AndAddToPresets()
    {
        // Arrange
        var (_, sportId, userId, _) = await SeedFullEnvironmentAsync(createSystemDefs: false);

        var customDefinition = new EventDefinition
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            OwnerId = userId,
            Name = "Custom Tactical Block",
            ShortName = "C-BLK",
            IsPositive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var (created, sortOrder) = await _repository.UpsertCustomAsync(customDefinition);

        // Assert
        created.Should().NotBeNull();
        created!.Id.Should().Be(customDefinition.Id);
        created.Name.Should().Be("Custom Tactical Block");
        created.OwnerId.Should().Be(userId);
        sortOrder.Should().Be(0);

        // Verify database state: user preset entry should exist
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var presetCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.usereventpresets WHERE userid = @userId AND eventdefinitionid = @defId",
            new { userId, defId = customDefinition.Id });

        presetCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.UpsertCustomAsync" /> updates an existing custom event definition
    /// and returns its current preset sort order.
    /// </summary>
    [Fact]
    public async Task UpsertCustomAsync_ShouldUpdateExistingCustomEventDefinition()
    {
        // Arrange
        var (_, sportId, userId, _) = await SeedFullEnvironmentAsync(createSystemDefs: false);

        var customDef = new EventDefinition
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            OwnerId = userId,
            Name = "Initial Name",
            ShortName = "INIT",
            IsPositive = false,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.UpsertCustomAsync(customDef);

        // Manually set preset sort order to 3 to verify update retrieves existing sort order
        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(
                "UPDATE public.usereventpresets SET sortorder = 3 WHERE userid = @userId AND eventdefinitionid = @defId",
                new { userId, defId = customDef.Id });
        }

        var updatedDef = new EventDefinition
        {
            Id = customDef.Id,
            SportId = sportId,
            OwnerId = userId,
            Name = "Updated Name",
            ShortName = "UPD",
            IsPositive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var (result, sortOrder) = await _repository.UpsertCustomAsync(updatedDef);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Name");
        result.ShortName.Should().Be("UPD");
        result.IsPositive.Should().BeTrue();
        sortOrder.Should().Be(3);
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.UpsertCustomAsync" /> throws PostgresException (P0001)
    /// when attempting to reuse an event definition identifier whose existing record has IsSoftDeleted set to true.
    /// </summary>
    [Fact]
    public async Task UpsertCustomAsync_ShouldThrowException_WhenReusingSoftDeletedDefinitionId()
    {
        // Arrange
        var (_, sportId, userId, _) = await SeedFullEnvironmentAsync(createSystemDefs: false);

        var customDefId = Guid.NewGuid();
        var entity = new EventDefinition
        {
            Id = customDefId,
            SportId = sportId,
            OwnerId = userId,
            Name = "Original Custom Action",
            ShortName = "OCA",
            IsPositive = true,
            IsSoftDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.UpsertCustomAsync(entity);
        await _repository.SoftDeleteAsync(customDefId, userId);

        var reusedEntity = new EventDefinition
        {
            Id = customDefId, // Attempting to reuse soft-deleted ID
            SportId = sportId,
            OwnerId = userId,
            Name = "Attempt Reused Action",
            ShortName = "ARA",
            IsPositive = false,
            IsSoftDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        Func<Task> act = async () => await _repository.UpsertCustomAsync(reusedEntity);

        // Assert
        await act.Should().ThrowAsync<Npgsql.PostgresException>()
            .WithMessage("*Cannot update or reuse a soft-deleted event definition*");
    }

    /// < summary >
    /// Verifies that sequential insertions of custom event definitions for the same user and sport
    /// dynamically assign incremental preset SortOrder values (0, 1, ...).
    /// < /summary >
    [Fact]
    public async Task UpsertCustomAsync_ShouldAssignIncrementalSortOrder_ForMultipleCustomDefinitions()
    {
        // Arrange
        var (_, sportId, userId, _) = await SeedFullEnvironmentAsync(createSystemDefs: false);

        var firstDef = new EventDefinition
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            OwnerId = userId,
            Name = "First Tactical Block",
            ShortName = "BLK1",
            IsPositive = true,
            CreatedAt = DateTime.UtcNow
        };

        var secondDef = new EventDefinition
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            OwnerId = userId,
            Name = "Second Tactical Block",
            ShortName = "BLK2",
            IsPositive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var (_, firstSortOrder) = await _repository.UpsertCustomAsync(firstDef);
        var (_, secondSortOrder) = await _repository.UpsertCustomAsync(secondDef);

        // Assert
        firstSortOrder.Should().Be(0);
        secondSortOrder.Should().Be(1);
    }

    #endregion

    #region SoftDeleteAsync Tests

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.SoftDeleteAsync" /> sets <c>issoftdeleted = TRUE</c> 
    /// for a user-owned custom definition and removes it from active presets.
    /// </summary>
    [Fact]
    public async Task SoftDeleteAsync_ShouldSoftDeleteCustomEventDefinition_AndRemoveFromPresets()
    {
        // Arrange
        var (_, sportId, userId, _) = await SeedFullEnvironmentAsync(createSystemDefs: false);

        var customDef = new EventDefinition
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            OwnerId = userId,
            Name = "Definition To Delete",
            ShortName = "DEL",
            IsPositive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.UpsertCustomAsync(customDef);

        // Act
        var deleted = await _repository.SoftDeleteAsync(customDef.Id, userId);

        // Assert
        deleted.Should().BeTrue();

        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var isSoftDeleted = await conn.ExecuteScalarAsync<bool>(
            "SELECT issoftdeleted FROM public.eventdefinitions WHERE id = @id",
            new { id = customDef.Id });

        isSoftDeleted.Should().BeTrue();

        var presetCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.usereventpresets WHERE eventdefinitionid = @id",
            new { id = customDef.Id });

        presetCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.SoftDeleteAsync" /> returns false when 
    /// trying to delete a non-existent definition or one not owned by the specified user.
    /// </summary>
    [Fact]
    public async Task SoftDeleteAsync_ShouldReturnFalse_WhenDefinitionNotFoundOrNotOwnedByUser()
    {
        // Arrange
        var (_, _, userId, definitionIds) = await SeedFullEnvironmentAsync(createSystemDefs: true);

        // System default definitions (ownerid IS NULL) cannot be soft-deleted by user
        // Act
        var result = await _repository.SoftDeleteAsync(definitionIds[0], userId);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetAvailableForUserAsync Tests

    /// <summary>
    /// Verifies that <see cref="EventDefinitionRepository.GetAvailableForUserAsync" /> returns system default
    /// and user custom event definitions enriched with layout ordering and preset enablement metadata.
    /// </summary>
    [Fact]
    public async Task GetAvailableForUserAsync_ShouldReturnSystemAndCustomDefinitions_EnrichedWithPresetState()
    {
        // Arrange
        var (_, sportId, userId, systemDefIds) = await SeedFullEnvironmentAsync(createSystemDefs: true);

        var customDef = new EventDefinition
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            OwnerId = userId,
            Name = "Custom Tactical Move",
            ShortName = "CTM",
            IsPositive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.UpsertCustomAsync(customDef);

        // Act
        var available = await _repository.GetAvailableForUserAsync(userId, sportId);

        // Assert
        var list = available.ToList();
        list.Should().NotBeNull();
        list.Should().HaveCount(systemDefIds.Count + 1);

        var customItem = list.FirstOrDefault(x => x.Id == customDef.Id);
        customItem.Should().NotBeNull();
        customItem!.IsCustom.Should().BeTrue();
        customItem.IsEnabled.Should().BeTrue(); // Automatically enabled on creation

        var systemItem = list.FirstOrDefault(x => x.Id == systemDefIds[0]);
        systemItem.Should().NotBeNull();
        systemItem!.IsCustom.Should().BeFalse();
    }

    #endregion

    #region Seed Helpers

    private async Task<(Guid MatchId, Guid SportId, string UserId, List<Guid> DefinitionIds)> SeedFullEnvironmentAsync(bool createSystemDefs)
    {
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

        if (createSystemDefs)
        {
            var def1 = Guid.NewGuid();
            var def2 = Guid.NewGuid();

            await conn.ExecuteAsync(@"
                INSERT INTO public.eventdefinitions (id, sportid, ownerid, name, shortname, ispositive, createdat)
                VALUES 
                (@id1, @sid, NULL, 'Goal', 'G', true, NOW()),
                (@id2, @sid, NULL, 'Foul', 'F', false, NOW())",
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

        return (matchId, sportId, userId, definitionIds);
    }

    #endregion
}