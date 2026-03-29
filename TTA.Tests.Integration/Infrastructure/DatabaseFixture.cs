using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using TTA.DataAccess.Repository.Base;

namespace TTA.Tests.Integration.Infrastructure;

public class DatabaseFixture : IAsyncLifetime
{
    // Pass the image name directly into the constructor
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tta_test_db")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    public IDbConnectionFactory ConnectionFactory { get; private set; } = null!;
    private Respawner _respawner = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        var connString = _dbContainer.GetConnectionString();

        ConnectionFactory = new TestConnectionFactory(connString);

        await ApplyMigrationsAsync(connString);

        // Prepare Respawner
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public", "auth"]
        });
    }

    private async Task ApplyMigrationsAsync(string connectionString)
    {
        // Adjust this path if your SQL scripts are in a different folder
        var scriptsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../TTA-backend/TTA.DataAccess/SQLScripts");

        if (!Directory.Exists(scriptsPath))
            throw new DirectoryNotFoundException($"SQL scripts folder not found at: {Path.GetFullPath(scriptsPath)}");

        var sqlFiles = Directory.GetFiles(scriptsPath, "*.sql").OrderBy(f => f);

        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var file in sqlFiles)
        {
            var sql = await File.ReadAllTextAsync(file);
            using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // Pass a connection instead of a string
    public async Task ResetDatabaseAsync()
    {
        using var conn = new NpgsqlConnection(_dbContainer.GetConnectionString());
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public async Task DisposeAsync() => await _dbContainer.StopAsync();
}