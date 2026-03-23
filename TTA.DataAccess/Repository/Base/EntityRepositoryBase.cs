using Dapper;
using Npgsql;
using System.Data;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Repository.Base;

/// <summary>
/// Abstract base class providing common data access logic for PostgreSQL repositories.
/// </summary>
/// <typeparam name="TKey">The type of the primary key.</typeparam>
/// <typeparam name="TEntity">The type of the entity.</typeparam>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public abstract class EntityRepositoryBase<TKey, TEntity>(IDbConnectionFactory connectionFactory) : IEntityRepositoryBase<TKey, TEntity>
    where TEntity : class, IKeyedEntity<TKey>, new()
    where TKey : IEquatable<TKey>
{
    /// <summary>
    /// Gets the name of the primary key parameter expected by PostgreSQL functions.
    /// Default is "p_id". Override this in derived classes if necessary.
    /// </summary>
    protected virtual string KeyParamName => "p_id";

    /// <summary>
    /// Creates and returns a new database connection.
    /// </summary>
    /// <returns>A new <see cref="IDbConnection"/> instance.</returns>
    protected IDbConnection GetConnection() => connectionFactory.CreateConnection();

    /// <inheritdoc />
    public async Task<TEntity> CreateOrUpdate(TEntity entity, string procName, DynamicParameters? additionalParams = null, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(ct);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var parameters = new DynamicParameters(entity);
            if (additionalParams != null)
            {
                parameters.AddDynamicParams(additionalParams);
            }

            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
            var result = await connection.QuerySingleAsync<TEntity>(command);

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TEntity?> GetById(TKey id, string procName, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        var parameters = new DynamicParameters();
        parameters.Add(KeyParamName, id);

        var command = new CommandDefinition(procName, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
        return await connection.QueryFirstOrDefaultAsync<TEntity>(command);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TEntity>> GetAll(string procName, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(procName, commandType: CommandType.StoredProcedure, cancellationToken: ct);
        return await connection.QueryAsync<TEntity>(command);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TEntity>> GetByPropValues(string procName, DynamicParameters parameters, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(procName, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
        return await connection.QueryAsync<TEntity>(command);
    }

    /// <inheritdoc />
    public async Task<string?> GetDataInJson(string procName, DynamicParameters parameters, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(procName, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
        return await connection.ExecuteScalarAsync<string>(command);
    }

    /// <inheritdoc />
    public async Task<bool> Exists(TKey id, string procName, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        var parameters = new DynamicParameters();
        parameters.Add(KeyParamName, id);

        var command = new CommandDefinition(procName, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
        return await connection.ExecuteScalarAsync<bool>(command);
    }

    /// <inheritdoc />
    public async Task<bool> Exists(string procName, DynamicParameters parameters, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(procName, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
        return await connection.ExecuteScalarAsync<bool>(command);
    }

    /// <inheritdoc />
    public async Task<bool> Delete(TKey id, string procName, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(ct);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var parameters = new DynamicParameters();
            parameters.Add(KeyParamName, id);

            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
            var affectedRows = await connection.ExecuteAsync(command);

            transaction.Commit();
            return affectedRows > 0;
        }
        catch
        {
            transaction.Rollback();
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ExecuteCommandInTransaction(string procName, DynamicParameters parameters, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(ct);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
            await connection.ExecuteAsync(command);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<T> ExecuteQueryInTransaction<T>(
    string commandTextOrProcName,
    DynamicParameters parameters,
    CommandType commandType = CommandType.StoredProcedure, // Added parameter with default value
    CancellationToken ct = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(ct);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            // Use the passed commandType instead of the hardcoded one
            var command = new CommandDefinition(commandTextOrProcName, parameters, transaction, commandType: commandType, cancellationToken: ct);
            var result = await connection.QuerySingleAsync<T>(command);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}