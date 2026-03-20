using Moq;
using System.Data;
using TTA.DataAccess.Models.Base;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Tests.Repository;

public class TestEntity : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
}

public class TestRepository(IDbConnectionFactory factory)
    : EntityRepositoryBase<Guid, TestEntity>(factory)
{ }

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

        // Setup the transaction and factory
        _mockConnection.Setup(c => c.BeginTransaction()).Returns(_mockTransaction.Object);
        _mockFactory.Setup(f => f.CreateConnection()).Returns(_mockConnection.Object);

        _repository = new TestRepository(_mockFactory.Object);
    }

    [Fact]
    public async Task CreateOrUpdate_ShouldHandleException_InCatchBlock()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid() };

        // Throwing error at BeginTransaction to avoid Dapper async conflicts
        _mockConnection.Setup(c => c.BeginTransaction())
                       .Throws(new Exception("Transaction start failed"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(async () =>
            await _repository.CreateOrUpdate(entity, "sp_test"));

        Assert.Equal("Transaction start failed", ex.Message);
    }

    [Fact]
    public async Task Delete_ShouldReturnFalse_OnException()
    {
        // Arrange
        // 1. Setup transaction to be returned successfully
        _mockConnection.Setup(c => c.BeginTransaction()).Returns(_mockTransaction.Object);

        // 2. Throw exception during command creation (this is where Dapper starts)
        // This ensures the transaction exists when the catch block calls Rollback()
        _mockConnection.Setup(c => c.CreateCommand())
                       .Throws(new Exception("DB Error"));

        // Act
        var result = await _repository.Delete(Guid.NewGuid(), "sp_delete_test");

        // Assert
        Assert.False(result);
        // Now this will pass because the transaction object was initialized
        _mockTransaction.Verify(t => t.Rollback(), Times.Once);
    }

    [Fact]
    public async Task ExecuteCommandInTransaction_ShouldThrow_OnError()
    {
        // Arrange
        _mockConnection.Setup(c => c.BeginTransaction())
                       .Throws(new Exception("Fatal error"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
            await _repository.ExecuteCommandInTransaction("sp_proc", new Dapper.DynamicParameters()));
    }

    [Fact]
    public async Task GetById_ShouldCallFactory()
    {
        // Act
        try { await _repository.GetById(Guid.NewGuid(), "sp_get"); } catch { }

        // Assert
        _mockFactory.Verify(f => f.CreateConnection(), Times.Once);
    }
}