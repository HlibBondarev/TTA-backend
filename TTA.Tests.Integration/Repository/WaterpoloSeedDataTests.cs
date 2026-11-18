using Dapper;
using FluentAssertions;
using TTA.DataAccess.Enums;
using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Integration tests verifying the Waterpolo disciplinary seed data (<c>maxpersonalfouls</c>, <c>foulimpact</c>
/// and <c>penaltydurationseconds</c>) produced by <c>03-Infrastructure-and-Seed.sql</c>.
/// </summary>
public class WaterpoloSeedDataTests(DatabaseFixture fixture) : BaseIntegrationTest(fixture)
{
    private static readonly Guid WaterpoloSportId = Guid.Parse("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f");

    private sealed record SeededDefinition(string Name, string ShortName, bool IsPositive, int FoulImpact, int? PenaltyDurationSeconds);

    /// <summary>
    /// Verifies that both Waterpolo configurations are seeded with <c>maxpersonalfouls = 3</c>.
    /// </summary>
    [Fact]
    public async Task Seed_ShouldSetMaxPersonalFoulsToThree_ForBothWaterpoloConfigurations()
    {
        // Arrange
        await ReapplySeedAsync();
        using var conn = Fixture.ConnectionFactory.CreateConnection();

        // Act
        var limits = (await conn.QueryAsync<int?>(
            "SELECT maxpersonalfouls FROM public.sportconfigurations WHERE sportid = @sportId",
            new { sportId = WaterpoloSportId })).ToList();

        // Assert
        limits.Should().HaveCount(2);
        limits.Should().OnlyContain(l => l == 3);
    }

    /// <summary>
    /// Verifies the exact foul impact and penalty duration mapping of the disciplinary Waterpolo event definitions.
    /// </summary>
    [Theory]
    [InlineData("Exclusion", "E", FoulImpact.PersonalFoul, 18)]
    [InlineData("Penalty Committed", "PC", FoulImpact.PersonalFoul, null)]
    [InlineData("Red Card (18s)", "RC18", FoulImpact.DisqualificationWithSub, 18)]
    [InlineData("Red Card (4 Min)", "RC4M", FoulImpact.DisqualificationWithSub, 240)]
    [InlineData("Red Card (Penalty)", "RCP", FoulImpact.DisqualificationWithSub, null)]
    public async Task Seed_ShouldAssignDisciplinaryAttributes_ToWaterpoloFoulEvents(
        string name, string shortName, FoulImpact expectedImpact, int? expectedDuration)
    {
        // Arrange
        await ReapplySeedAsync();

        // Act
        var definition = await GetWaterpoloDefinitionsAsync();
        var row = definition.Single(d => d.Name == name && d.ShortName == shortName && !d.IsPositive);

        // Assert
        row.FoulImpact.Should().Be((int)expectedImpact);
        row.PenaltyDurationSeconds.Should().Be(expectedDuration);
    }

    /// <summary>
    /// Verifies that only the five disciplinary events carry a non-default foul impact and that every other
    /// Waterpolo event (positive actions, Exclusion Earned, Penalty Earned, Offensive Foul, Ball Under, etc.)
    /// is seeded with <c>FoulImpact.None</c> and no penalty duration, with expected polarity for every definition.
    /// </summary>
    [Fact]
    public async Task Seed_ShouldAssignNoneAndNullDuration_ToAllOtherWaterpoloEvents()
    {
        // Arrange
        await ReapplySeedAsync();
        var disciplinaryShortNames = new[] { "E", "PC", "RC18", "RC4M", "RCP" };

        // Act
        var definitions = await GetWaterpoloDefinitionsAsync();
        var others = definitions.Where(d => !disciplinaryShortNames.Contains(d.ShortName) || d.IsPositive).ToList();

        // Assert
        definitions.Should().HaveCount(34);
        others.Should().HaveCount(29);
        others.Should().OnlyContain(d => d.FoulImpact == (int)FoulImpact.None && d.PenaltyDurationSeconds == null);

        definitions.Where(d => d.IsPositive)
            .Select(d => d.ShortName)
            .Should().BeEquivalentTo(["G", "ST", "SP", "DA", "SOG", "ASV", "FB", "A", "FT", "EE", "CAS", "SW", "PE"]);

        definitions.Where(d => !d.IsPositive)
            .Select(d => d.ShortName)
            .Should().BeEquivalentTo(["GC", "TO", "BP", "PEO", "PED", "PB", "OF", "MS", "SB", "GKE", "E", "STO", "STD", "CAC", "BU", "SL", "PC", "RC18", "RC4M", "RCP", "OG"]);
    }

    /// <summary>
    /// Verifies that the legacy single "Red Card" definition has been replaced by three distinct red card definitions.
    /// </summary>
    [Fact]
    public async Task Seed_ShouldContainThreeRedCardDefinitions_AndNoLegacyRedCard()
    {
        // Arrange
        await ReapplySeedAsync();

        // Act
        var definitions = await GetWaterpoloDefinitionsAsync();

        // Assert
        definitions.Should().NotContain(d => d.Name == "Red Card");
        definitions.Where(d => d.Name.StartsWith("Red Card")).Should().HaveCount(3);
    }

    private async Task<List<SeededDefinition>> GetWaterpoloDefinitionsAsync()
    {
        using var conn = Fixture.ConnectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<SeededDefinition>(@"
            SELECT name AS Name, shortname AS ShortName, ispositive AS IsPositive,
                   foulimpact AS FoulImpact, penaltydurationseconds AS PenaltyDurationSeconds
            FROM public.eventdefinitions
            WHERE sportid = @sportId AND ownerid IS NULL AND issoftdeleted = FALSE",
            new { sportId = WaterpoloSportId });

        return rows.ToList();
    }

    /// <summary>
    /// Resets the database (removing any seed already applied by the shared fixture) and re-applies the seed script.
    /// </summary>
    private async Task ReapplySeedAsync()
    {
        await Fixture.ResetDatabaseAsync();

        var seedPath = FindSeedScript();
        var sql = await File.ReadAllTextAsync(seedPath);

        using var conn = Fixture.ConnectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql);
    }

    private static string FindSeedScript()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "TTA.DataAccess", "SQLScripts", "03-Infrastructure-and-Seed.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate 03-Infrastructure-and-Seed.sql walking up from the test output directory.");
    }
}