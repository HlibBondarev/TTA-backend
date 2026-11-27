using Dapper;
using System.Data.Common;

namespace TTA.Tests.Integration.Infrastructure;

// This attribute ensures that all tests using this base class 
// belong to the same collection and share the same DatabaseFixture instance.
[Collection("DatabaseCollection")]
public abstract class BaseIntegrationTest(DatabaseFixture fixture) : IAsyncLifetime
{
    protected readonly DatabaseFixture Fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    // Reset the database state after each test
    public virtual async Task DisposeAsync() => await Fixture.ResetDatabaseAsync();

    /// <summary>
    /// Seeds a sport and its default configuration record atomically within a single transaction
    /// to satisfy the deferred foreign key constraint <c>fk_sports_default_config</c>.
    /// Uses Dapper async execution with an asynchronous transaction.
    /// </summary>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="name">The name of the sport.</param>
    /// <param name="shortName">The short code of the sport.</param>
    /// <param name="configId">The unique identifier of the configuration.</param>
    protected async Task SeedSportWithConfigAsync(Guid sportId, string name, string shortName, Guid configId)
    {
        await using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string sportSql = @"
            INSERT INTO public.sports (id, name, shortname, defaultconfigid) 
            VALUES (@sportId, @name, @shortName, @configId)";

        await conn.ExecuteAsync(sportSql, new { sportId, name, shortName, configId }, transaction: tx);

        const string configSql = @"
            INSERT INTO public.sportconfigurations (
                id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit, playground, fieldlength, fieldwidth
            ) VALUES (
                @configId, @sportId, false, 2, 45, 'Standard', 25, 11, 7, 'Playground', 25, 20)";

        await conn.ExecuteAsync(configSql, new { configId, sportId }, transaction: tx);

        await tx.CommitAsync();
    }

    /// <summary>
    /// Seeds the Quick Match sandbox that <c>public.create_quick_match</c> relies on for a sport configuration:
    /// the sandbox owner, base geography, the default club, <c>Goalkeeper</c> / <c>Utility</c> positions (if the sport has none),
    /// the shared <c>Home Squad</c> and <c>Opponent Squad</c> teams (reused when they already exist for the sport),
    /// a JIT tournament bound to the configuration, and a roster of <paramref name="rosterSize"/> players per team
    /// (cap <c>1</c> is the goalkeeper, all other caps are utility players).
    /// The sport and its configuration must already exist (see <see cref="SeedSportWithConfigAsync"/>).
    /// </summary>
    /// <param name="sportId">The unique identifier of an existing sport.</param>
    /// <param name="configurationId">The unique identifier of an existing configuration of the sport.</param>
    /// <param name="rosterSize">The number of roster entries seeded for each team.</param>
    /// <returns>The sandbox tournament, home team, guest team and tournament owner identifiers.</returns>
    protected async Task<(Guid TournamentId, Guid HomeTeamId, Guid GuestTeamId, string OwnerId)> SeedQuickMatchSandboxAsync(
        Guid sportId,
        Guid configurationId,
        int rosterSize = 5)
    {
        var clubId = Guid.Parse("11111111-1111-1111-1111-000000000001");
        var cityId = Guid.Parse("c0000000-0000-0000-0000-000000000005");
        var ownerId = $"auth0|sandbox-owner-{Guid.NewGuid():N}";
        var tournamentId = Guid.NewGuid();

        await using var conn = (DbConnection)Fixture.ConnectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync(
            "INSERT INTO public.users (id, email, displayname, createdat) VALUES (@ownerId, @email, 'Sandbox Owner', NOW())",
            new { ownerId, email = $"{Guid.NewGuid():N}@sandbox.test" }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO public.countries (name, code) VALUES ('country', 'CTR') ON CONFLICT DO NOTHING;
            INSERT INTO public.regions (countryid, name)
                SELECT id, 'region' FROM public.countries WHERE code = 'CTR' ON CONFLICT DO NOTHING;
            INSERT INTO public.cities (id, regionid, name)
                SELECT @cityId, id, 'city' FROM public.regions WHERE name = 'region' ON CONFLICT DO NOTHING;
            INSERT INTO public.clubs (id, cityid, name, createdat)
                VALUES (@clubId, @cityId, 'TTA Training Club', NOW()) ON CONFLICT DO NOTHING;",
            new { cityId, clubId }, tx);

        var goalkeeperId = Guid.NewGuid();
        var utilityId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname) VALUES
                (@goalkeeperId, @sportId, 'Goalkeeper', 'GK'),
                (@utilityId, @sportId, 'Utility', 'UTL')",
            new { goalkeeperId, utilityId, sportId }, tx);

        var homeTeamId = await GetOrCreateSandboxTeamAsync(conn, tx, clubId, sportId, "Home Squad");
        var guestTeamId = await GetOrCreateSandboxTeamAsync(conn, tx, clubId, sportId, "Opponent Squad");

        await conn.ExecuteAsync(@"
            INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, isjit, startdate, createdat)
            VALUES (@tournamentId, @sportId, @configurationId, @cityId, @ownerId, 'Training & Friendly Matches', TRUE, NOW(), NOW())",
            new { tournamentId, sportId, configurationId, cityId, ownerId }, tx);

        await SeedSandboxRosterAsync(conn, tx, clubId, tournamentId, homeTeamId, rosterSize, goalkeeperId, utilityId);
        await SeedSandboxRosterAsync(conn, tx, clubId, tournamentId, guestTeamId, rosterSize, goalkeeperId, utilityId);

        await tx.CommitAsync();

        return (tournamentId, homeTeamId, guestTeamId, ownerId);
    }

    private static async Task<Guid> GetOrCreateSandboxTeamAsync(
        DbConnection conn, DbTransaction tx, Guid clubId, Guid sportId, string name)
    {
        var existingId = await conn.QueryFirstOrDefaultAsync<Guid?>(
            "SELECT id FROM public.teams WHERE clubid = @clubId AND sportid = @sportId AND name = @name LIMIT 1",
            new { clubId, sportId, name }, tx);

        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var teamId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat) VALUES (@teamId, @clubId, @sportId, @name, 0, NOW())",
            new { teamId, clubId, sportId, name }, tx);

        return teamId;
    }

    private static async Task SeedSandboxRosterAsync(
        DbConnection conn,
        DbTransaction tx,
        Guid clubId,
        Guid tournamentId,
        Guid teamId,
        int rosterSize,
        Guid goalkeeperId,
        Guid utilityId)
    {
        for (var cap = 1; cap <= rosterSize; cap++)
        {
            var playerId = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
                VALUES (@playerId, @clubId, 'Sandbox Player', @lastName, '2000-01-01', 0, NOW())",
                new { playerId, clubId, lastName = cap.ToString() }, tx);

            await conn.ExecuteAsync(@"
                INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat)
                VALUES (@id, @playerId, @tournamentId, @teamId, @cap, @positionId, NOW())",
                new
                {
                    id = Guid.NewGuid(),
                    playerId,
                    tournamentId,
                    teamId,
                    cap,
                    positionId = cap == 1 ? goalkeeperId : utilityId
                }, tx);
        }
    }
}

// Definition of the shared fixture
[CollectionDefinition("DatabaseCollection", DisableParallelization = true)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }