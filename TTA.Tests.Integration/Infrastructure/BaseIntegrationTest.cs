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
    public async Task DisposeAsync() => await Fixture.ResetDatabaseAsync();

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
                id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit
            ) VALUES (
                @configId, @sportId, false, 2, 45, 'Standard', 25, 11, 7)";

        await conn.ExecuteAsync(configSql, new { configId, sportId }, transaction: tx);

        await tx.CommitAsync();
    }
}

// Definition of the shared fixture
[CollectionDefinition("DatabaseCollection", DisableParallelization = true)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }