using Dapper;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Repository.Base;

/// <summary>
/// Provides a generic abstraction for data access operations using Dapper and PostgreSQL functions.
/// Supports CRUD operations, complex queries, and transaction-based execution.
/// </summary>
/// <typeparam name="TKey">The type of the unique identifier (must implement <see cref="IEquatable{TKey}"/>).</typeparam>
/// <typeparam name="TEntity">The type of the domain entity (must implement <see cref="IKeyedEntity{TKey}"/>).</typeparam>
public interface IEntityRepositoryBase<TKey, TEntity>
    where TEntity : class, IKeyedEntity<TKey>, new()
    where TKey : IEquatable<TKey>
{
    /// <summary>
    /// Creates a new entity or updates an existing one by executing a database function.
    /// Runs within a database transaction.
    /// </summary>
    /// <param name="entity">The entity containing data to be saved.</param>
    /// <param name="procName">The name of the PostgreSQL function.</param>
    /// <param name="additionalParams">Optional extra parameters to pass to the function.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entity as returned by the database function after the operation.</returns>
    Task<TEntity> CreateOrUpdate(TEntity entity, string procName, DynamicParameters? additionalParams = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single entity by its unique identifier using a database function.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="procName">The name of the PostgreSQL function (expects a parameter named after <c>KeyParamName</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TEntity?> GetById(TKey id, string procName, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all entities of type <typeparamref name="TEntity"/> from the specified function.
    /// </summary>
    /// <param name="procName">The name of the PostgreSQL function.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IEnumerable<TEntity>> GetAll(string procName, CancellationToken ct = default);

    /// <summary>
    /// Retrieves entities based on specific property values.
    /// </summary>
    /// <param name="procName">The name of the PostgreSQL function.</param>
    /// <param name="parameters">Dynamic parameters for the search criteria.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IEnumerable<TEntity>> GetByPropValues(string procName, DynamicParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Executes a function and returns the result as a JSON string.
    /// Useful for complex reports or tree structures.
    /// </summary>
    Task<string?> GetDataInJson(string procName, DynamicParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Checks if an entity exists by its unique identifier.
    /// </summary>
    Task<bool> Exists(TKey id, string procName, CancellationToken ct = default);

    /// <summary>
    /// Checks if an entity exists based on custom criteria.
    /// </summary>
    Task<bool> Exists(string procName, DynamicParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Deletes an entity by its identifier within a transaction.
    /// </summary>
    /// <returns><c>true</c> if the entity was successfully deleted; otherwise, <c>false</c>.</returns>
    Task<bool> Delete(TKey id, string procName, CancellationToken ct = default);

    /// <summary>
    /// Executes a non-query command (like an update or complex action) within a transaction.
    /// </summary>
    Task ExecuteCommandInTransaction(string procName, DynamicParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Executes a query that returns a scalar result of type <typeparamref name="T"/> within a transaction.
    /// </summary>
    /// <typeparam name="T">The type of the result (e.g., long, int, string).</typeparam>
    Task<T> ExecuteQueryInTransaction<T>(string procName, DynamicParameters parameters, CancellationToken ct = default);
}