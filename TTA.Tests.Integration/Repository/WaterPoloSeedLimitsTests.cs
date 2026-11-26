using FluentAssertions;
using System.Text.RegularExpressions;

namespace TTA.Tests.Integration.Repository;

/// <summary>
/// Verifies the water polo sport configuration seed in <c>03-Infrastructure-and-Seed.sql</c>.
/// The script text is asserted directly because the shared test database is reset (seed rows removed)
/// after every integration test, which would make a database-level assertion order dependent.
/// </summary>
public class WaterPoloSeedLimitsTests
{
    private const string SeedFileName = "03-Infrastructure-and-Seed.sql";
    private const int ExpectedLimit = 15;

    /// <summary>
    /// Verifies both seeded water polo configurations use roster and lineup limits of 15.
    /// </summary>
    /// <param name="fieldSize">The field size label identifying the configuration.</param>
    /// <param name="expectedActivePlayers">The expected active players limit (unchanged by this alignment).</param>
    [Theory]
    [InlineData("25x20 sq.m.", 7)]
    [InlineData("25x15 sq.m.", 6)]
    public void WaterPoloSeed_ShouldUseRosterAndLineupLimitOfFifteen(string fieldSize, int expectedActivePlayers)
    {
        // Arrange
        var sql = File.ReadAllText(Path.Combine(FindSqlScriptsPath(), SeedFileName));
        var pattern = $@"'{Regex.Escape(fieldSize)}',\s*(?<roster>\d+),\s*(?<lineup>\d+),\s*(?<active>\d+),";

        // Act
        var matches = Regex.Matches(sql, pattern);

        // Assert
        matches.Should().HaveCount(1, "each water polo configuration is seeded exactly once");
        var match = matches[0];
        int.Parse(match.Groups["roster"].Value).Should().Be(ExpectedLimit);
        int.Parse(match.Groups["lineup"].Value).Should().Be(ExpectedLimit);
        int.Parse(match.Groups["active"].Value).Should().Be(expectedActivePlayers);
    }

    private static string FindSqlScriptsPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "TTA.DataAccess", "SQLScripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find TTA.DataAccess/SQLScripts walking up from {AppContext.BaseDirectory}.");
    }
}
