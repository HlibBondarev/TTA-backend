using Dapper;
using FluentAssertions;
using System.Data.Common;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests for the <see cref="TimeAnchorRepository"/>.
/// Validates data access logic and PostgreSQL storage function integration for match timelines.
/// </summary>
public class TimeAnchorRepositoryTests : BaseIntegrationTest
{
    private readonly TimeAnchorRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeAnchorRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared database fixture.</param>
    public TimeAnchorRepositoryTests(DatabaseFixture fixture) : base(fixture)
    {
        _repository = new TimeAnchorRepository(fixture.ConnectionFactory);
    }

    #region Integration Tests

    /// <summary>
    /// Verifies that <see cref="TimeAnchorRepository.UpsertAsync"/> correctly persists a new time anchor.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_ShouldPersistNewAnchor_WhenDataIsValid()
    {
        // Arrange
        var matchId = await SeedTimeAnchorEnvironmentAsync();
        var timeAnchor = CreateAnchorModel(matchId, 1, (TimeAnchorType)0); // 0 = PeriodStart

        // Act
        var result = await _repository.UpsertAsync(timeAnchor);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(timeAnchor.Id);
        result.MatchId.Should().Be(matchId);

        var persisted = await _repository.GetByIdAsync(timeAnchor.Id);
        persisted.Should().NotBeNull();
        persisted!.Timestamp.Should().BeCloseTo(timeAnchor.Timestamp, TimeSpan.FromMilliseconds(1));
    }

    /// <summary>
    /// Verifies that <see cref="TimeAnchorRepository.GetByIdAsync"/> retrieves an existing time anchor.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity_WhenExists()
    {
        // Arrange
        var matchId = await SeedTimeAnchorEnvironmentAsync();
        var timeAnchor = CreateAnchorModel(matchId, 1, (TimeAnchorType)0);
        await _repository.UpsertAsync(timeAnchor);

        // Act
        var result = await _repository.GetByIdAsync(timeAnchor.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(timeAnchor.Id);
        result.PeriodNumber.Should().Be(timeAnchor.PeriodNumber);
        result.Type.Should().Be(timeAnchor.Type);
    }

    /// <summary>
    /// Verifies that <see cref="TimeAnchorRepository.GetMatchAnchorsAsync"/> returns all anchors for a specific match.
    /// </summary>
    [Fact]
    public async Task GetMatchAnchorsAsync_ShouldReturnTimeline_ForSpecificMatch()
    {
        // Arrange
        var matchId = await SeedTimeAnchorEnvironmentAsync();

        var anchor1 = CreateAnchorModel(matchId, 1, (TimeAnchorType)0); // PeriodStart
        var anchor2 = CreateAnchorModel(matchId, 1, (TimeAnchorType)1); // PeriodEnd
        // Ensure chronological difference
        anchor2.Timestamp = anchor1.Timestamp.AddMinutes(45);

        await _repository.UpsertAsync(anchor1);
        await _repository.UpsertAsync(anchor2);

        // Act
        var timeline = await _repository.GetMatchAnchorsAsync(matchId);

        // Assert
        var anchors = timeline.ToList();
        anchors.Should().OnlyContain(a => a.MatchId == matchId);
        anchors.Should().Contain(a => a.Id == anchor1.Id);
        anchors.Should().Contain(a => a.Id == anchor2.Id);
        anchors.FindIndex(a => a.Id == anchor1.Id)
            .Should().BeLessThan(anchors.FindIndex(a => a.Id == anchor2.Id));
    }

    /// <summary>
    /// Verifies that a time anchor is successfully removed from the database.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ShouldRemoveAnchor_WhenExists()
    {
        // Arrange
        var matchId = await SeedTimeAnchorEnvironmentAsync();
        var newAnchor = CreateAnchorModel(matchId, 1, (TimeAnchorType)0);

        // Ensure record exists before deletion
        await _repository.UpsertAsync(newAnchor);

        // Act
        var isDeleted = await _repository.DeleteAsync(newAnchor.Id);
        var deletedCheck = await _repository.GetByIdAsync(newAnchor.Id);

        // Assert
        isDeleted.Should().BeTrue("Repository must return true when the record is successfully deleted");
        deletedCheck.Should().BeNull("The record should no longer exist in the database");
    }

    /// <summary>
    /// Verifies that <see cref="TimeAnchorRepository.GetMatchPeriodDurationMinutesAsync"/> retrieves the correct nominal duration when the match exists.
    /// </summary>
    [Fact]
    public async Task GetMatchPeriodDurationMinutesAsync_ShouldReturnConfiguredMinutes_WhenMatchExists()
    {
        // Arrange
        var matchId = await SeedTimeAnchorEnvironmentAsync();

        // Act
        var duration = await _repository.GetMatchPeriodDurationMinutesAsync(matchId);

        // Assert
        // The seed helper environment initializes a sport configuration with a 45-minute nominal period duration.
        duration.Should().Be(45);
    }

    /// <summary>
    /// Verifies that <see cref="TimeAnchorRepository.GetMatchPeriodDurationMinutesAsync"/> returns zero when the specified match does not exist.
    /// </summary>
    [Fact]
    public async Task GetMatchPeriodDurationMinutesAsync_ShouldReturnZero_WhenMatchDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();

        // Act
        var duration = await _repository.GetMatchPeriodDurationMinutesAsync(nonExistentMatchId);

        // Assert
        duration.Should().Be(0);
    }

    #endregion

    #region Seed Helpers

    /// <summary>
    /// Seeds the environment for integration tests.
    /// Creates the minimum required hierarchy up to a valid Match record.
    /// Uses explicit transaction to satisfy deferred FK constraints and updated Sport table schema.
    /// </summary>
    /// <returns>The unique identifier of the created match.</returns>
    private async Task<Guid> SeedTimeAnchorEnvironmentAsync()
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

        // Sport & Config (Generated unique per seed execution)
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var sportName = $"Sport_{suffix}";
        var shortName = suffix[..3].ToUpper();

        await conn.ExecuteAsync(@"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@id, @name, @shortName, @configId)",
            new { id = sportId, name = sportName, shortName, configId }, transaction: transaction);

        await conn.ExecuteAsync(@"
            INSERT INTO public.sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit) 
            VALUES (@id, @sid, false, 2, 45, '105x68', 25, 11)",
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

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, createdat) 
            VALUES (@id, @tid, @teamid, @teamid, NOW(), NOW())",
            new { id = matchId, tid = tournamentId, teamid = teamId }, transaction: transaction);

        await transaction.CommitAsync();

        return matchId;
    }

    /// <summary>
    /// Creates a standard <see cref="TimeAnchor"/> model for testing.
    /// </summary>
    private static TimeAnchor CreateAnchorModel(Guid matchId, int periodNumber, TimeAnchorType type)
    {
        return new TimeAnchor
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PeriodNumber = periodNumber,
            Type = type,
            // Uses UTC as strictly requested by project rules
            Timestamp = DateTime.UtcNow
        };
    }

    #endregion
}