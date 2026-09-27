using Dapper;
using Npgsql;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for user event definition presets using PostgreSQL storage functions.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class UserEventPresetRepository(IDbConnectionFactory connectionFactory) : IUserEventPresetRepository
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;

    /// <inheritdoc />
    public async Task SavePresetAsync(string userId, Guid sportId, IEnumerable<Guid> eventDefinitionIds, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_user_id", userId);
        parameters.Add("p_sport_id", sportId);
        parameters.Add("p_event_definition_ids", eventDefinitionIds.ToArray());

        using var connection = _connectionFactory.CreateConnection();
        if (connection is NpgsqlConnection npgsqlConn)
        {
            await npgsqlConn.OpenAsync(cancellationToken);
        }
        else
        {
            connection.Open();
        }

        await connection.ExecuteAsync(new CommandDefinition(
            SqlStatements.ForUserEventPresets.SaveUserEventPreset,
            parameters,
            cancellationToken: cancellationToken));
    }
}