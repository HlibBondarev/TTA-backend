using Npgsql;
using System.Data;
using TTA.DataAccess.Repository.Base;

namespace TTA.Tests.Integration.Infrastructure;

// Simplified factory for tests that takes connection string directly
public class TestConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);
}