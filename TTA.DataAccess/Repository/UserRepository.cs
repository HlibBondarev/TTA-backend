using Dapper;
using Npgsql;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for managing User entities in PostgreSQL.
/// Inherits from <see cref="EntityRepositoryBase{string, User}"/> for common CRUD operations.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class UserRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<string, User>(connectionFactory), IUserRepository
{
    /// <inheritdoc />
    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        return await GetById(id, SqlStatements.ForUsers.GetUserById, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<User>> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_email", email);

        return await GetByPropValues(
            SqlStatements.ForUsers.GetUsersByEmail,
            parameters,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(User User, bool IsInserted)> UpsertAsync(User user, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        if (connection is NpgsqlConnection npgsqlConn) await npgsqlConn.OpenAsync(cancellationToken);
        else connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var parameters = new DynamicParameters();
            parameters.Add("p_id", user.Id);
            parameters.Add("p_email", user.Email);
            parameters.Add("p_displayname", user.DisplayName);
            parameters.Add("p_createdat", user.CreatedAt);

            var command = new CommandDefinition(
                SqlStatements.ForUsers.UpsertUser,
                parameters,
                transaction,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            var result = await connection.QuerySingleAsync<UserWithInsertionFlag>(command);
            transaction.Commit();

            var mappedUser = new User
            {
                Id = result.Out_Id,
                Email = result.Out_Email,
                DisplayName = result.Out_Displayname,
                CreatedAt = result.Out_Createdat
            };

            return (mappedUser, result.Is_Inserted);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await Delete(id, SqlStatements.ForUsers.DeleteUser, cancellationToken);
    }

    /// <summary>
    /// Internal immutable projection record used by Dapper to map output parameters 
    /// returned by the <c>public.upsert_user</c> stored procedure.
    /// </summary>
    /// <param name="Out_Id">The unique identifier of the user mapped from the <c>out_id</c> database column.</param>
    /// <param name="Out_Email">The email address of the user mapped from the <c>out_email</c> database column.</param>
    /// <param name="Out_Displayname">The display name of the user mapped from the <c>out_displayname</c> database column.</param>
    /// <param name="Out_Createdat">The timestamp when the user was created mapped from the <c>out_createdat</c> database column.</param>
    /// <param name="Is_Inserted">A boolean flag indicating whether a new record was inserted (<c>true</c>) or updated (<c>false</c>) mapped from the <c>is_inserted</c> database column.</param>
    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Parameter names match PostgreSQL output column names for direct Dapper constructor mapping.")]
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Parameter names match PostgreSQL output column names for direct Dapper constructor mapping.")]
    private sealed record UserWithInsertionFlag(
        string Out_Id,
        string Out_Email,
        string Out_Displayname,
        DateTime Out_Createdat,
        bool Is_Inserted);
}