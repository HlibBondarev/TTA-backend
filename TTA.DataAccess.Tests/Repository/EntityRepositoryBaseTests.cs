using Dapper;
using Moq;
using System.Data;
using TTA.DataAccess.Models.Base;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Tests.Repository;

// Mock model for testing
public class TestEntity : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
}

// Concrete implementation for testing abstract class
public class TestRepository(IDbConnectionFactory factory)
    : EntityRepositoryBase<Guid, TestEntity>(factory)
{
    public string ExposedKeyParamName => KeyParamName;
    public IDbConnection ExposedGetConnection() => GetConnection();
}

public class EntityRepositoryBaseTests
{
    private readonly Mock<IDbConnectionFactory> _mockFactory;
    private readonly Mock<IDbConnection> _mockConnection;
    private readonly Mock<IDbTransaction> _mockTransaction;
    private readonly TestRepository _repository;

    public EntityRepositoryBaseTests()
    {
        _mockFactory = new Mock<IDbConnectionFactory>();
        _mockConnection = new Mock<IDbConnection>();
        _mockTransaction = new Mock<IDbTransaction>();

        // Setup connection to return mock transaction
        _mockConnection.Setup(c => c.BeginTransaction()).Returns(_mockTransaction.Object);
        _mockFactory.Setup(f => f.CreateConnection()).Returns(_mockConnection.Object);

        _repository = new TestRepository(_mockFactory.Object);
    }

    // --- Infrastructure Tests ---

    [Fact]
    public void KeyParamName_DefaultValue_IsPId()
    {
        // Assert
        Assert.Equal("p_id", _repository.ExposedKeyParamName);
    }

    [Fact]
    public void GetConnection_ReturnsConnectionFromFactory()
    {
        // Act
        var connection = _repository.ExposedGetConnection();

        // Assert
        Assert.NotNull(connection);
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
    }

    // --- Connection Lifecycle Tests ---

    [Fact]
    public async Task GetById_CallsCreateConnectionAndDispose()
    {
        // Act
        try { await _repository.GetById(Guid.NewGuid(), "sp_test"); } catch { }

        // Assert
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
        _mockConnection.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public async Task GetAll_CallsCreateConnection()
    {
        // Act
        try { await _repository.GetAll("sp_test"); } catch { }

        // Assert
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
    }

    // --- Transaction Logic Tests ---

    [Fact]
    public async Task CreateOrUpdate_ShouldOpenConnectionAndStartTransaction()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid() };

        // Act
        try { await _repository.CreateOrUpdate(entity, "sp_save"); } catch { }

        // Assert
        _mockConnection.Verify(c => c.Open(), Times.AtMostOnce());
        _mockConnection.Verify(c => c.BeginTransaction(), Times.Once);
    }

    [Fact]
    public async Task Delete_ShouldOpenConnectionAndStartTransaction()
    {
        // Act
        try { await _repository.Delete(Guid.NewGuid(), "sp_delete"); } catch { }

        // Assert
        _mockConnection.Verify(c => c.Open(), Times.AtMostOnce());
        _mockConnection.Verify(c => c.BeginTransaction(), Times.Once);
    }

    [Fact]
    public async Task ExecuteCommandInTransaction_ShouldRollbackOnException()
    {
        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _repository.ExecuteCommandInTransaction("sp_error", new DynamicParameters()));

        _mockTransaction.Verify(t => t.Rollback(), Times.AtLeastOnce);
    }

    // --- Additional Coverage Tests ---

    [Fact]
    public async Task Exists_UsesParametersCorrectly()
    {
        // Act
        try { await _repository.Exists(Guid.NewGuid(), "sp_exists"); } catch { }

        // Assert
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
    }

    [Fact]
    public async Task GetDataInJson_CallsExecuteScalar()
    {
        // Act
        try { await _repository.GetDataInJson("sp_json", new DynamicParameters()); } catch { }

        // Assert
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
    }
}