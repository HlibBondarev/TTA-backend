using Microsoft.Extensions.Configuration;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Tests.Repository;

public class NpgsqlConnectionFactoryTests
{
    [Fact]
    public void CreateConnection_ReturnsValidNpgsqlConnection()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string> {
            {"ConnectionStrings:DefaultConnection", "Host=localhost;Database=test_db"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();

        var factory = new NpgsqlConnectionFactory(configuration);

        // Act
        var connection = factory.CreateConnection();

        // Assert
        // Ensuring the factory produces a connection object with the right string
        Assert.NotNull(connection);
        Assert.Contains("Host=localhost", connection.ConnectionString);
    }
}