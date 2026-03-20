using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Data;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Repository.Base;

/// <summary>
/// Abstract base class providing common data access logic for PostgreSQL repositories.
/// </summary>
public abstract class EntityRepositoryBase<TKey, TEntity>(IConfiguration configuration) : IEntityRepositoryBase<TKey, TEntity>
    where TEntity : class, IKeyedEntity<TKey>, new()
    where TKey : IEquatable<TKey>
{
    protected readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection connection string is missing.");

    /// <summary>
    /// Gets the name of the primary key parameter expected by PostgreSQL functions.
    /// Default is "p_id". Override this in derived classes if necessary.
    /// </summary>
    protected virtual string KeyParamName => "p_id";

    private NpgsqlConnection GetConnection() => new(_connectionString);

    /// <inheritdoc />
    public async Task<TEntity> CreateOrUpdate(TEntity entity, string procName, DynamicParameters? additionalParams = null, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            var parameters = new DynamicParameters(entity);
            if (additionalParams != null)
            {
                parameters.AddDynamicParams(additionalParams);
            }

            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
            var result = await connection.QuerySingleAsync<TEntity>(command);

            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
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
        await connection.OpenAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            var parameters = new DynamicParameters();
            parameters.Add(KeyParamName, id);

            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
            var affectedRows = await connection.ExecuteAsync(command);

            await transaction.CommitAsync(ct);
            return affectedRows > 0;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ExecuteCommandInTransaction(string procName, DynamicParameters parameters, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
            await connection.ExecuteAsync(command);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<T> ExecuteQueryInTransaction<T>(string procName, DynamicParameters parameters, CancellationToken ct = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            var command = new CommandDefinition(procName, parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
            var result = await connection.QuerySingleAsync<T>(command);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}