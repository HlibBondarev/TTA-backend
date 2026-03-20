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

        // Setup connection lifecycle
        _mockConnection.Setup(c => c.BeginTransaction()).Returns(_mockTransaction.Object);
        _mockFactory.Setup(f => f.CreateConnection()).Returns(_mockConnection.Object);

        _repository = new TestRepository(_mockFactory.Object);
    }

    // --- Infrastructure Tests ---

    [Fact]
    public void KeyParamName_DefaultValue_IsPId()
    {
        Assert.Equal("p_id", _repository.ExposedKeyParamName);
    }

    // --- Transaction & Rollback Tests ---

    [Fact]
    public async Task CreateOrUpdate_ShouldRollbackAndThrow_OnExecutionError()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid() };

        // We simulate an error at the IDbConnection level (CreateCommand) 
        // because Moq cannot mock Dapper's extension methods directly.
        _mockConnection.Setup(c => c.CreateCommand())
                       .Throws(new Exception("Database execution failed"));

        // Act & Assert
        // This will trigger the 'catch' block in EntityRepositoryBase
        await Assert.ThrowsAsync<Exception>(async () =>
            await _repository.CreateOrUpdate(entity, "sp_test_procedure"));

        // Verify that Rollback was called exactly once in the catch block
        _mockTransaction.Verify(t => t.Rollback(), Times.Once);
    }

    [Fact]
    public async Task Delete_ShouldRollbackAndReturnFalse_OnException()
    {
        // Arrange
        _mockConnection.Setup(c => c.CreateCommand())
                       .Throws(new Exception("Delete operation failed"));

        // Act
        // Your Delete method returns 'false' in the catch block instead of re-throwing
        var result = await _repository.Delete(Guid.NewGuid(), "sp_delete_procedure");

        // Assert
        Assert.False(result);
        _mockTransaction.Verify(t => t.Rollback(), Times.Once);
    }

    // --- Lifecycle Tests (Basic Coverage) ---

    [Fact]
    public async Task GetById_CallsCreateConnection()
    {
        try { await _repository.GetById(Guid.NewGuid(), "sp_get"); } catch { }
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
    }

    [Fact]
    public async Task GetAll_CallsCreateConnection()
    {
        try { await _repository.GetAll("sp_all"); } catch { }
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
    }
}