using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Data;

namespace TTA.DataAccess.Repository.Base;

/// <summary>
/// Production implementation of the connection factory for PostgreSQL.
/// </summary>
public class NpgsqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection connection string is missing.");

    /// <inheritdoc />
    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}
