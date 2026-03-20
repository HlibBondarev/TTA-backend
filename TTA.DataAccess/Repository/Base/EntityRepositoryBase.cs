using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Data;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Repository.Base;

public abstract class EntityRepositoryBase<TKey, TEntity>(IConfiguration configuration) : IEntityRepositoryBase<TKey, TEntity>
    where TEntity : class, IKeyedEntity<TKey>, new()
    where TKey : IEquatable<TKey>
{
    protected readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection connection string is missing.");

    // Helper to create connection
    private NpgsqlConnection GetConnection() => new(_connectionString);

    public async Task<TEntity> CreateOrUpdate(TEntity entity, string procName, Dictionary<string, object>? additionalParams = null)
    {
        using var connection = GetConnection();
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var parameters = new DynamicParameters(entity);
            if (additionalParams != null)
            {
                foreach (var pair in additionalParams)
                {
                    parameters.Add(pair.Key, pair.Value);
                }
            }

            // In PostgreSQL, functions returning records are called via SELECT * FROM function_name(@params)
            // But with CommandType.StoredProcedure, Dapper handles the call syntax.
            var result = await connection.QuerySingleAsync<TEntity>(
                sql: procName,
                param: parameters,
                transaction: transaction,
                commandType: CommandType.StoredProcedure
            );

            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<TEntity?> GetById(TKey id, string procName)
    {
        using var connection = GetConnection();
        return await connection.QueryFirstOrDefaultAsync<TEntity>(
            sql: procName,
            param: new { p_id = id },
            commandType: CommandType.StoredProcedure
        );
    }

    public async Task<IEnumerable<TEntity>> GetAll(string procName)
    {
        using var connection = GetConnection();
        return await connection.QueryAsync<TEntity>(
            sql: procName,
            commandType: CommandType.StoredProcedure
        );
    }

    public async Task<IEnumerable<TEntity>> GetByPropValues(string procName, Dictionary<string, object> parameters)
    {
        using var connection = GetConnection();
        return await connection.QueryAsync<TEntity>(
            sql: procName,
            param: new DynamicParameters(parameters),
            commandType: CommandType.StoredProcedure
        );
    }

    public async Task<string?> GetDataInJson(string procName, Dictionary<string, object> parameters)
    {
        using var connection = GetConnection();
        return await connection.ExecuteScalarAsync<string>(
            sql: procName,
            param: new DynamicParameters(parameters),
            commandType: CommandType.StoredProcedure
        );
    }

    public async Task<bool> Exists(TKey id, string procName)
    {
        using var connection = GetConnection();
        return await connection.ExecuteScalarAsync<bool>(
            sql: procName,
            param: new { p_id = id },
            commandType: CommandType.StoredProcedure
        );
    }

    public async Task<bool> Exists(string procName, Dictionary<string, object> parameters)
    {
        using var connection = GetConnection();
        return await connection.ExecuteScalarAsync<bool>(
            sql: procName,
            param: new DynamicParameters(parameters),
            commandType: CommandType.StoredProcedure
        );
    }

    public async Task<bool> Delete(TKey id, string procName)
    {
        using var connection = GetConnection();
        var affectedRows = await connection.ExecuteAsync(
            sql: procName,
            param: new { p_id = id },
            commandType: CommandType.StoredProcedure
        );
        return affectedRows > 0;
    }

    public async Task ExecuteCommandInTransaction(string procName, Dictionary<string, object> parameters)
    {
        using var connection = GetConnection();
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await connection.ExecuteAsync(
                sql: procName,
                param: new DynamicParameters(parameters),
                transaction: transaction,
                commandType: CommandType.StoredProcedure
            );
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<long> ExecuteQueryInTransaction(string procName, DynamicParameters parameters)
    {
        using var connection = GetConnection();
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var result = await connection.QuerySingleAsync<long>(
                sql: procName,
                param: parameters,
                transaction: transaction,
                commandType: CommandType.StoredProcedure
            );
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
