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
    public async Task<TEntity> CreateOrUpdate(TEntity entity, string sqlText, DynamicParameters? additionalParams = null, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(cancellationToken);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var parameters = new DynamicParameters(entity);
            if (additionalParams != null)
            {
                parameters.AddDynamicParams(additionalParams);
            }

            var command = new CommandDefinition(sqlText, parameters, transaction, commandType: CommandType.Text, cancellationToken: cancellationToken);
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
    public async Task<TEntity?> GetById(TKey id, string sqlText, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        var parameters = new DynamicParameters();
        parameters.Add(KeyParamName, id);

        var command = new CommandDefinition(sqlText, parameters, commandType: CommandType.Text, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<TEntity>(command);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TEntity>> GetAll(string sqlText, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(sqlText, commandType: CommandType.Text, cancellationToken: cancellationToken);
        return await connection.QueryAsync<TEntity>(command);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TEntity>> GetByPropValues(string sqlText, DynamicParameters parameters, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(sqlText, parameters, commandType: CommandType.Text, cancellationToken: cancellationToken);
        return await connection.QueryAsync<TEntity>(command);
    }

    /// <inheritdoc />
    public async Task<string?> GetDataInJson(string sqlText, DynamicParameters parameters, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(sqlText, parameters, commandType: CommandType.Text, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<string>(command);
    }

    /// <inheritdoc />
    public async Task<bool> Exists(TKey id, string sqlText, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        var parameters = new DynamicParameters();
        parameters.Add(KeyParamName, id);

        var command = new CommandDefinition(sqlText, parameters, commandType: CommandType.Text, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(command);
    }

    /// <inheritdoc />
    public async Task<bool> Exists(string sqlText, DynamicParameters parameters, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        var command = new CommandDefinition(sqlText, parameters, commandType: CommandType.Text, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(command);
    }

    /// <inheritdoc />
    public async Task<bool> Delete(TKey id, string sqlText, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", id);

        using var connection = await OpenConnectionAsync(cancellationToken);

        // ExecuteScalarAsync retrieves the first column of the first row (our INT return)
        var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sqlText,
            parameters,
            cancellationToken: cancellationToken));

        return result > 0;
    }

    /// <inheritdoc />
    public async Task ExecuteCommandInTransaction(string procName, DynamicParameters parameters, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(cancellationToken);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
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
    string sqlText,
    DynamicParameters parameters,
    CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(cancellationToken);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            // Use the passed commandType instead of the hardcoded one
            var command = new CommandDefinition(sqlText, parameters, transaction, commandType: CommandType.Text, cancellationToken: cancellationToken);
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

    /// <inheritdoc />
    public async Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = GetConnection();

        // Ensure the connection is opened asynchronously for Npgsql
        if (connection is NpgsqlConnection npgsqlConn)
        {
            await npgsqlConn.OpenAsync(cancellationToken);
        }
        else
        {
            connection.Open();
        }

        return connection;
    }

    /// <inheritdoc />
    public async Task ExecuteCommandAsync(
        string sqlText,
        DynamicParameters parameters,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        // Define the command to run within the existing transaction context
        var command = new CommandDefinition(
            sqlText,
            parameters,
            transaction,
            commandType: CommandType.Text,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}