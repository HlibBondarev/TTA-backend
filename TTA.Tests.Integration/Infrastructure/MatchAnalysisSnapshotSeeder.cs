using Dapper;
using Npgsql;
using TTA.DataAccess.Repository.Base;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// Identifiers of the data created by <see cref="MatchAnalysisSnapshotSeeder"/>.
/// </summary>
/// <param name="MatchId">The seeded match.</param>
/// <param name="HomeTeamId">The home team (has lineup entries).</param>
/// <param name="GuestTeamId">The guest team (has exactly one lineup entry).</param>
/// <param name="SportId">The seeded sport.</param>
/// <param name="TournamentConfigurationId">The configuration actually used by the tournament.</param>
/// <param name="DefaultConfigurationId">The sport default configuration (NOT used by the tournament).</param>
/// <param name="SystemEventDefinitionIds">Active system event definitions that must be returned.</param>
/// <param name="ExcludedEventDefinitionIds">Soft-deleted system and custom definitions that must be excluded.</param>
/// <param name="ExternalTeamId">A team that does not participate in the match.</param>
public sealed record SeededMatchAnalysis(
    Guid MatchId,
    Guid HomeTeamId,
    Guid GuestTeamId,
    Guid SportId,
    Guid TournamentConfigurationId,
    Guid DefaultConfigurationId,
    IReadOnlyList<Guid> SystemEventDefinitionIds,
    IReadOnlyList<Guid> ExcludedEventDefinitionIds,
    Guid ExternalTeamId);

/// <summary>
/// Seeds a complete match analysis scenario (sport with two configurations, tournament bound to the non-default
/// configuration, teams, lineups and mixed event definitions) in a single transaction.
/// </summary>
public static class MatchAnalysisSnapshotSeeder
{
    private const string OwnerId = "auth0|snapshot-owner";

    /// <summary>
    /// Seeds the scenario.
    /// </summary>
    /// <param name="factory">The connection factory of the test database.</param>
    /// <param name="homeLineupCount">The number of lineup entries of the home team.</param>
    /// <returns>The identifiers of the seeded entities.</returns>
    public static async Task<SeededMatchAnalysis> SeedAsync(IDbConnectionFactory factory, int homeLineupCount = 2)
    {
        await using var conn = (NpgsqlConnection)factory.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var cityId = await SeedLocationAndOwnerAsync(conn, tx);
        var (sportId, defaultConfigId, tournamentConfigId) = await SeedSportAsync(conn, tx);
        var positionIds = await SeedPositionsAsync(conn, tx, sportId);
        var (systemIds, excludedIds) = await SeedEventDefinitionsAsync(conn, tx, sportId);

        var clubId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.clubs (id, cityid, name, createdat) VALUES (@clubId, @cityId, @name, NOW())",
            new { clubId, cityId, name = $"Club_{clubId:N}" }, tx);

        var tournamentId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat)
            VALUES (@tournamentId, @sportId, @tournamentConfigId, @cityId, @OwnerId, @name, NOW(), NOW())",
            new { tournamentId, sportId, tournamentConfigId, cityId, OwnerId, name = $"Cup_{tournamentId:N}" }, tx);

        var homeTeamId = await SeedTeamAsync(conn, tx, clubId, sportId, "Home FC");
        var guestTeamId = await SeedTeamAsync(conn, tx, clubId, sportId, "Guest FC");
        var externalTeamId = await SeedTeamAsync(conn, tx, clubId, sportId, "External FC");

        var matchId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.matches (id, tournamentid, hometeamid, guestteamid, scheduledat, matchnumber, venue, createdat)
            VALUES (@matchId, @tournamentId, @homeTeamId, @guestTeamId, NOW(), 'M-SNAP', 'Main Pool', NOW())",
            new { matchId, tournamentId, homeTeamId, guestTeamId }, tx);

        await SeedLineupAsync(conn, tx, new LineupSeed(matchId, tournamentId, homeTeamId, clubId, positionIds, homeLineupCount));
        await SeedLineupAsync(conn, tx, new LineupSeed(matchId, tournamentId, guestTeamId, clubId, positionIds, 1));

        await tx.CommitAsync();

        return new SeededMatchAnalysis(
            matchId, homeTeamId, guestTeamId, sportId, tournamentConfigId, defaultConfigId, systemIds, excludedIds, externalTeamId);
    }

    private sealed record LineupSeed(
        Guid MatchId, Guid TournamentId, Guid TeamId, Guid ClubId, IReadOnlyList<Guid> PositionIds, int Count);

    private static async Task<int> SeedCountryRegionAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await conn.ExecuteAsync(
            "INSERT INTO public.countries (name, code) VALUES ('Ukraine', 'UA') ON CONFLICT (name) DO NOTHING", transaction: tx);
        var countryId = await conn.QuerySingleAsync<int>("SELECT id FROM public.countries WHERE name = 'Ukraine'", transaction: tx);

        await conn.ExecuteAsync(
            "INSERT INTO public.regions (countryid, name) VALUES (@countryId, 'Snapshot Region') ON CONFLICT (countryid, name) DO NOTHING",
            new { countryId }, tx);
        return await conn.QuerySingleAsync<int>(
            "SELECT id FROM public.regions WHERE countryid = @countryId AND name = 'Snapshot Region'", new { countryId }, tx);
    }

    private static async Task<Guid> SeedLocationAndOwnerAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var regionId = await SeedCountryRegionAsync(conn, tx);

        var cityId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.cities (id, regionid, name) VALUES (@cityId, @regionId, 'Snapshot City')",
            new { cityId, regionId }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO public.users (id, email, displayname, createdat)
            VALUES (@OwnerId, 'snapshot-owner@tta.com', 'Snapshot Owner', NOW())
            ON CONFLICT (id) DO NOTHING",
            new { OwnerId }, tx);

        return cityId;
    }

    private static async Task<(Guid SportId, Guid DefaultConfigId, Guid TournamentConfigId)> SeedSportAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var sportId = Guid.NewGuid();
        var defaultConfigId = Guid.NewGuid();
        var tournamentConfigId = Guid.NewGuid();

        await conn.ExecuteAsync(
            "INSERT INTO public.sports (id, name, shortname, defaultconfigid) VALUES (@sportId, @name, 'SNP', @defaultConfigId)",
            new { sportId, name = $"Snapshot Sport {sportId:N}", defaultConfigId }, tx);

        const string configSql = @"
            INSERT INTO public.sportconfigurations
                (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit,
                 activeplayerslimit, playground, shotmap, fieldlength, fieldwidth, maxpersonalfouls)
            VALUES
                (@id, @sportId, @cleanTime, @periods, @duration, @fieldSize, @roster, @lineup, @active,
                 @playground, @shotmap, @length, @width, @fouls)";

        await conn.ExecuteAsync(configSql, new
        {
            id = defaultConfigId, sportId, cleanTime = false, periods = 2, duration = 45, fieldSize = "DEFAULT",
            roster = 11, lineup = 11, active = 11, playground = "<svg>default</svg>", shotmap = (string?)null,
            length = 100m, width = 50m, fouls = (int?)null
        }, tx);

        await conn.ExecuteAsync(configSql, new
        {
            id = tournamentConfigId, sportId, cleanTime = true, periods = 4, duration = 8, fieldSize = "25x20 sq.m.",
            roster = 15, lineup = 15, active = 7, playground = "<svg>playground</svg>", shotmap = "<svg>shotmap</svg>",
            length = 25m, width = 20m, fouls = (int?)3
        }, tx);

        return (sportId, defaultConfigId, tournamentConfigId);
    }

    private static async Task<IReadOnlyList<Guid>> SeedPositionsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid sportId)
    {
        var centerForward = Guid.NewGuid();
        var driver = Guid.NewGuid();

        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES
            (@centerForward, @sportId, 'Center Forward', 'CF'),
            (@driver, @sportId, 'Driver', 'D')",
            new { centerForward, driver, sportId }, tx);

        return [centerForward, driver];
    }

    private static async Task<(IReadOnlyList<Guid> System, IReadOnlyList<Guid> Excluded)> SeedEventDefinitionsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid sportId)
    {
        var goal = Guid.NewGuid();
        var exclusion = Guid.NewGuid();
        var softDeleted = Guid.NewGuid();
        var custom = Guid.NewGuid();

        const string sql = @"
            INSERT INTO public.eventdefinitions
                (id, sportid, ownerid, name, shortname, ispositive, isshot, score, foulimpact, penaltydurationseconds, issoftdeleted, createdat)
            VALUES (@id, @sportId, @ownerId, @name, @shortName, @isPositive, @isShot, @score, @foulImpact, @penalty, @isSoftDeleted, NOW())";

        var rows = new[]
        {
            new { id = goal, ownerId = (string?)null, name = "Goal", shortName = "G", isPositive = true, isShot = true, score = 1, foulImpact = 0, penalty = (int?)null, isSoftDeleted = false },
            new { id = exclusion, ownerId = (string?)null, name = "Exclusion", shortName = "E", isPositive = false, isShot = false, score = 0, foulImpact = 1, penalty = (int?)18, isSoftDeleted = false },
            new { id = softDeleted, ownerId = (string?)null, name = "Retired", shortName = "RET", isPositive = true, isShot = false, score = 0, foulImpact = 0, penalty = (int?)null, isSoftDeleted = true },
            new { id = custom, ownerId = (string?)OwnerId, name = "Custom Action", shortName = "CUS", isPositive = true, isShot = false, score = 0, foulImpact = 0, penalty = (int?)null, isSoftDeleted = false }
        };

        foreach (var row in rows)
        {
            await conn.ExecuteAsync(sql, new
            {
                row.id, sportId, row.ownerId, row.name, row.shortName, row.isPositive, row.isShot,
                row.score, row.foulImpact, row.penalty, row.isSoftDeleted
            }, tx);
        }

        return ([goal, exclusion], [softDeleted, custom]);
    }

    private static async Task<Guid> SeedTeamAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid clubId, Guid sportId, string name)
    {
        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@teamId, @clubId, @sportId, @name, 0, NOW())",
            new { teamId, clubId, sportId, name }, tx);
        return teamId;
    }

    private static async Task SeedLineupAsync(NpgsqlConnection conn, NpgsqlTransaction tx, LineupSeed seed)
    {
        for (var i = 1; i <= seed.Count; i++)
        {
            var playerId = Guid.NewGuid();
            var rosterId = Guid.NewGuid();
            var positionId = seed.PositionIds[(i - 1) % seed.PositionIds.Count];

            await conn.ExecuteAsync(@"
                INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
                VALUES (@playerId, @ClubId, @firstName, 'Player', '2000-01-01', 0, NOW())",
                new { playerId, seed.ClubId, firstName = $"P{i}" }, tx);

            await conn.ExecuteAsync(@"
                INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat)
                VALUES (@rosterId, @playerId, @TournamentId, @TeamId, @number, @positionId, NOW())",
                new { rosterId, playerId, seed.TournamentId, seed.TeamId, number = i, positionId }, tx);

            await conn.ExecuteAsync(@"
                INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid)
                VALUES (@id, @MatchId, @rosterId, @number, @positionId)",
                new { id = Guid.NewGuid(), seed.MatchId, rosterId, number = i, positionId }, tx);
        }
    }
}
