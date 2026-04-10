using Npgsql;
using System.Data;
using TTA.DataAccess.Repository.Base;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// A specialized implementation of <see cref="IDbConnectionFactory"/> designed for integration testing.
/// It ensures that connection strings are properly configured for a test environment,
/// specifically disabling transaction enlistment to allow manual transaction control.
/// </summary>
public class TestConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestConnectionFactory"/> class.
    /// </summary>
    /// <param name="connectionString">The raw connection string provided by the test container or configuration.</param>
    public TestConnectionFactory(string connectionString)
    {
        // Use NpgsqlConnectionStringBuilder for robust and case-insensitive string manipulation.
        // This replaces brittle string checks and ensures 'Enlist' is always disabled.
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // Explicitly set Enlist to false to prevent the connection from automatically 
            // joining an ambient TransactionScope, which is critical for our 
            // explicit transaction management in integration tests.
            Enlist = false
        };

        _connectionString = builder.ConnectionString;
    }

    /// <summary>
    /// Creates and returns a new <see cref="NpgsqlConnection"/> using the pre-configured test connection string.
    /// </summary>
    /// <returns>An initialized <see cref="IDbConnection"/> object.</returns>
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }
}