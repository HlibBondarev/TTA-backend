using Dapper;
using FluentAssertions;
using System.Data.Common;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for the <see cref="UserEventPresetRepository"/>.
/// Validates PostgreSQL stored function integration for saving, updating, and resetting user event presets.
/// </summary>
public class UserEventPresetRepositoryTests : BaseIntegrationTest
{
    private readonly UserEventPresetRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserEventPresetRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture.</param>
    public UserEventPresetRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new UserEventPresetRepository(fixture.ConnectionFactory);
    }

    #region SavePresetAsync Tests

    /// <summary>
    /// Verifies that <see cref="UserEventPresetRepository.SavePresetAsync"/> successfully inserts new user event presets
    /// with matching sort ordering when no prior presets exist.
    /// </summary>
    [Fact]
    public async Task SavePresetAsync_ShouldInsertNewPresetSelection_WithCorrectSortOrder()
    {
        // Arrange
        var (userId, sportId, eventDefIds) = await SeedPresetEnvironmentAsync(defCount: 3);
        var selectedIds = new[] { eventDefIds[2], eventDefIds[0] }; // Select 2 items in specific order

        // Act
        await _repository.SavePresetAsync(userId, sportId, selectedIds);

        // Assert
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var presets = (await conn.QueryAsync<PresetRecord>(
            "SELECT userid, eventdefinitionid, sortorder FROM public.usereventpresets WHERE userid = @userId ORDER BY sortorder ASC",
            new { userId })).ToList();

        presets.Should().HaveCount(2);

        presets[0].EventDefinitionId.Should().Be(eventDefIds[2]);
        presets[0].SortOrder.Should().Be(0);

        presets[1].EventDefinitionId.Should().Be(eventDefIds[0]);
        presets[1].SortOrder.Should().Be(1);
    }

    /// <summary>
    /// Verifies that <see cref="UserEventPresetRepository.SavePresetAsync"/> overwrites and replaces existing user event presets 
    /// when called with a updated collection of event definition identifiers.
    /// </summary>
    [Fact]
    public async Task SavePresetAsync_ShouldOverwriteExistingPreset_WhenUserSavesNewLayout()
    {
        // Arrange
        var (userId, sportId, eventDefIds) = await SeedPresetEnvironmentAsync(defCount: 3);

        // Initial save
        await _repository.SavePresetAsync(userId, sportId, new[] { eventDefIds[0], eventDefIds[1] });

        // Act - Save new layout with different items and ordering
        var newSelectedIds = new[] { eventDefIds[2], eventDefIds[1], eventDefIds[0] };
        await _repository.SavePresetAsync(userId, sportId, newSelectedIds);

        // Assert
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var presets = (await conn.QueryAsync<PresetRecord>(
            "SELECT userid, eventdefinitionid, sortorder FROM public.usereventpresets WHERE userid = @userId ORDER BY sortorder ASC",
            new { userId })).ToList();

        presets.Should().HaveCount(3);

        presets[0].EventDefinitionId.Should().Be(eventDefIds[2]);
        presets[0].SortOrder.Should().Be(0);

        presets[1].EventDefinitionId.Should().Be(eventDefIds[1]);
        presets[1].SortOrder.Should().Be(1);

        presets[2].EventDefinitionId.Should().Be(eventDefIds[0]);
        presets[2].SortOrder.Should().Be(2);
    }

    /// <summary>
    /// Verifies that <see cref="UserEventPresetRepository.SavePresetAsync"/> clears all preset entries 
    /// for a given user and sport when an empty collection of identifiers is provided.
    /// </summary>
    [Fact]
    public async Task SavePresetAsync_ShouldClearPreset_WhenEmptyListIsProvided()
    {
        // Arrange
        var (userId, sportId, eventDefIds) = await SeedPresetEnvironmentAsync(defCount: 2);

        // Initial save
        await _repository.SavePresetAsync(userId, sportId, eventDefIds);

        // Act - Pass empty collection
        await _repository.SavePresetAsync(userId, sportId, Enumerable.Empty<Guid>());

        // Assert
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.usereventpresets WHERE userid = @userId",
            new { userId });

        count.Should().Be(0);
    }

    /// <summary>
    /// Verifies that <see cref="UserEventPresetRepository.SavePresetAsync"/> respects cancellation token requests.
    /// </summary>
    [Fact]
    public async Task SavePresetAsync_ShouldRespectCancellationToken()
    {
        // Arrange
        var (userId, sportId, eventDefIds) = await SeedPresetEnvironmentAsync(defCount: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _repository.SavePresetAsync(userId, sportId, eventDefIds, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that SavePresetAsync raises a PostgresException (P0001) when attempting to save 
    /// an event definition ID that belongs to another user or a different sport scope.
    /// </summary>
    [Fact]
    public async Task SavePresetAsync_ShouldThrowException_WhenEventDefinitionIsUnauthorizedOrInvalid()
    {
        // Arrange
        var (userId, sportId, _) = await SeedPresetEnvironmentAsync(defCount: 1);
        var otherUserId = $"auth0|other-user-{Guid.NewGuid():N}";
        var foreignDefId = Guid.NewGuid();

        using (var conn = Fixture.ConnectionFactory.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO public.users (id, email, displayname, createdat) 
                VALUES (@userId, 'other@tta.com', 'Other User', NOW());

                INSERT INTO public.eventdefinitions (id, sportid, ownerid, name, shortname, ispositive, createdat)
                VALUES (@defId, @sportId, @userId, 'Foreign Action', 'FRG', true, NOW());",
                new { userId = otherUserId, defId = foreignDefId, sportId });
        }

        // Act
        Func<Task> act = async () => await _repository.SavePresetAsync(userId, sportId, new[] { foreignDefId });

        // Assert
        await act.Should().ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "P0001");
    }

    /// <summary>
    /// Verifies that concurrent calls to <see cref="UserEventPresetRepository.SavePresetAsync"/> for the same user and sport 
    /// are correctly serialized by Postgres advisory locks without throwing exceptions, leaving the final preset state matching 
    /// exactly one of the requested preset collections.
    /// </summary>
    [Fact]
    public async Task SavePresetAsync_ShouldHandleConcurrentUpdates_WithoutRaceConditions()
    {
        // Arrange
        var (userId, sportId, definitionIds) = await SeedPresetEnvironmentAsync(defCount: 2);
        var preset1 = new[] { definitionIds[0] };
        var preset2 = new[] { definitionIds[1] };

        // Act
        Func<Task> act = async () =>
        {
            var task1 = Task.Run(async () =>
            {
                var repo = new UserEventPresetRepository(Fixture.ConnectionFactory);
                await repo.SavePresetAsync(userId, sportId, preset1);
            });

            var task2 = Task.Run(async () =>
            {
                var repo = new UserEventPresetRepository(Fixture.ConnectionFactory);
                await repo.SavePresetAsync(userId, sportId, preset2);
            });

            await Task.WhenAll(task1, task2);
        };

        // Assert: Ensure execution completes without exceptions
        await act.Should().NotThrowAsync();

        // Assert: Verify database integrity (final state must strictly equal preset1 OR preset2)
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var actualPresetDefIds = (await conn.QueryAsync<Guid>(
            "SELECT eventdefinitionid FROM public.usereventpresets WHERE userid = @userId ORDER BY sortorder ASC",
            new { userId })).ToList();

        var matchesPreset1 = actualPresetDefIds.SequenceEqual(preset1);
        var matchesPreset2 = actualPresetDefIds.SequenceEqual(preset2);

        (matchesPreset1 || matchesPreset2).Should().BeTrue(
            "the final preset state must match exactly preset1 or preset2 without partial row mixing");
    }

    #endregion

    #region Seed Helpers

    private async Task<(string UserId, Guid SportId, List<Guid> EventDefIds)> SeedPresetEnvironmentAsync(int defCount)
    {
        using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        // 1. User Setup
        var userId = $"auth0|preset-tester-{Guid.NewGuid():N}";
        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat) 
            VALUES (@id, @email, 'Tester', NOW())",
            new { id = userId, email = $"{userId}@tta.com" },
            transaction: transaction);

        // 2. Sport & SportConfiguration Setup
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

        // 3. Event Definitions Setup
        var eventDefIds = new List<Guid>();
        for (int i = 0; i < defCount; i++)
        {
            var defId = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO public.eventdefinitions (id, sportid, ownerid, name, shortname, ispositive, createdat)
                VALUES (@id, @sid, NULL, @name, @shortName, true, NOW())",
                new { id = defId, sid = sportId, name = $"Event_{i}_{defId:N}", shortName = $"E{i}" },
                transaction: transaction);

            eventDefIds.Add(defId);
        }

        await transaction.CommitAsync();

        return (userId, sportId, eventDefIds);
    }

    private record PresetRecord(string UserId, Guid EventDefinitionId, int SortOrder);

    #endregion
}