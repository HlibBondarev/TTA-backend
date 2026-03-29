namespace TTA.Tests.Integration.Infrastructure;

// This attribute ensures that all tests using this base class 
// belong to the same collection and share the same DatabaseFixture instance.
[Collection("DatabaseCollection")]
public abstract class BaseIntegrationTest(DatabaseFixture fixture) : IAsyncLifetime
{
    protected readonly DatabaseFixture Fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    // Reset the database state after each test
    public async Task DisposeAsync() => await Fixture.ResetDatabaseAsync();
}

// Definition of the shared fixture
[CollectionDefinition("DatabaseCollection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }