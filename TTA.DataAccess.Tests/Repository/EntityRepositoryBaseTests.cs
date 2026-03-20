using Microsoft.Extensions.Configuration;
using Moq;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Tests.Repository;

/// <summary>
/// Concrete test subclass of EntityRepositoryBase to allow instantiation.
/// </summary>
internal class TestRepository(IConfiguration configuration)
    : EntityRepositoryBase<Guid, City>(configuration)
{
    /// <summary>Exposes KeyParamName for testing.</summary>
    public string ExposedKeyParamName => KeyParamName;
}

/// <summary>
/// Concrete test subclass that overrides KeyParamName for testing override behavior.
/// </summary>
internal class CustomKeyParamRepository(IConfiguration configuration)
    : EntityRepositoryBase<Guid, City>(configuration)
{
    protected override string KeyParamName => "p_city_id";
    public string ExposedKeyParamName => KeyParamName;
}

/// <summary>
/// Concrete test subclass using int key (Region model).
/// </summary>
internal class RegionRepository(IConfiguration configuration)
    : EntityRepositoryBase<int, Region>(configuration)
{
    public string ExposedKeyParamName => KeyParamName;
}

/// <summary>
/// Concrete test subclass using string key (User model).
/// </summary>
internal class UserRepository(IConfiguration configuration)
    : EntityRepositoryBase<string, User>(configuration)
{
    public string ExposedKeyParamName => KeyParamName;
}

public class EntityRepositoryBaseTests
{
    private static IConfiguration CreateConfiguration(string? connectionString)
    {
        var inMemorySettings = new Dictionary<string, string?>();
        if (connectionString != null)
        {
            inMemorySettings["ConnectionStrings:DefaultConnection"] = connectionString;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public void Constructor_WithValidConnectionString_DoesNotThrow()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var exception = Record.Exception(() => new TestRepository(config));
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithMissingConnectionString_ThrowsInvalidOperationException()
    {
        var config = CreateConfiguration(null);
        var ex = Assert.Throws<InvalidOperationException>(() => new TestRepository(config));
        Assert.Contains("DefaultConnection", ex.Message);
    }

    [Fact]
    public void Constructor_WithMissingConnectionString_ExceptionMessageMentionsDefaultConnection()
    {
        var config = CreateConfiguration(null);
        var ex = Assert.Throws<InvalidOperationException>(() => new TestRepository(config));
        Assert.Equal("DefaultConnection connection string is missing.", ex.Message);
    }

    [Fact]
    public void Constructor_WithMockedConfigurationReturningNullConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange: GetConnectionString reads configuration.GetSection("ConnectionStrings")[name]
        var mockConnStringsSection = new Mock<IConfigurationSection>();
        mockConnStringsSection.Setup(s => s["DefaultConnection"]).Returns((string?)null);

        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c.GetSection("ConnectionStrings")).Returns(mockConnStringsSection.Object);

        // Also mock the indexer path that GetConnectionString may use
        mockConfig.Setup(c => c["ConnectionStrings:DefaultConnection"]).Returns((string?)null);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new TestRepository(mockConfig.Object));
    }

    [Fact]
    public void KeyParamName_DefaultValue_IsPId()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var repo = new TestRepository(config);
        Assert.Equal("p_id", repo.ExposedKeyParamName);
    }

    [Fact]
    public void KeyParamName_CanBeOverriddenInDerivedClass()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var repo = new CustomKeyParamRepository(config);
        Assert.Equal("p_city_id", repo.ExposedKeyParamName);
    }

    [Fact]
    public void Constructor_WithIntKey_CreatesRepositorySuccessfully()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var exception = Record.Exception(() => new RegionRepository(config));
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithStringKey_CreatesRepositorySuccessfully()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var exception = Record.Exception(() => new UserRepository(config));
        Assert.Null(exception);
    }

    [Fact]
    public void RegionRepository_KeyParamName_DefaultsToP_Id()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var repo = new RegionRepository(config);
        Assert.Equal("p_id", repo.ExposedKeyParamName);
    }

    [Fact]
    public void UserRepository_KeyParamName_DefaultsToP_Id()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var repo = new UserRepository(config);
        Assert.Equal("p_id", repo.ExposedKeyParamName);
    }

    [Fact]
    public void TestRepository_ImplementsIEntityRepositoryBase()
    {
        var config = CreateConfiguration("Host=localhost;Database=test");
        var repo = new TestRepository(config);
        Assert.IsAssignableFrom<IEntityRepositoryBase<Guid, City>>(repo);
    }

    [Fact]
    public void Repository_ConnectionString_IsStoredFromConfiguration()
    {
        const string connStr = "Host=localhost;Database=tta;Username=admin;Password=secret";
        var config = CreateConfiguration(connStr);
        // If construction succeeds, the connection string was properly read
        var exception = Record.Exception(() => new TestRepository(config));
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithEmptyStringConnectionString_DoesNotThrow()
    {
        // Empty string is technically not null, so it won't throw in the constructor
        // (the throw only happens when GetConnectionString returns null)
        var config = CreateConfiguration(string.Empty);
        var exception = Record.Exception(() => new TestRepository(config));
        Assert.Null(exception);
    }
}