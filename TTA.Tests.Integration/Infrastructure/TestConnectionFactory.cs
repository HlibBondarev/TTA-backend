using Npgsql;
using System.Data;
using TTA.DataAccess.Repository.Base;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// Simplified factory for tests that takes connection string directly.
/// </summary>
public class TestConnectionFactory(string connectionString) : IDbConnectionFactory
{
    // Force Enlist=false to prevent Npgsql from attempting to escalate to a distributed transaction (DTC),
    // which is not supported in Postgres/Docker environments.
    private readonly string _connectionString = connectionString.Contains("Enlist=")
        ? connectionString
        : $"{connectionString};Enlist=false";

    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}