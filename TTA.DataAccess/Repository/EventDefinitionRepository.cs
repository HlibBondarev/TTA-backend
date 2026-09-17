using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Implements data access operations for event definitions using PostgreSQL storage functions.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class EventDefinitionRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, EventDefinition>(connectionFactory), IEventDefinitionRepository
{
    private const string UserIdParameter = "p_user_id";

    /// <inheritdoc />
    public async Task<(EventDefinition? Definition, int SortOrder)> UpsertCustomAsync(EventDefinition entity, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", entity.Id);
        parameters.Add("p_sport_id", entity.SportId);
        parameters.Add("p_owner_id", entity.OwnerId);
        parameters.Add("p_name", entity.Name);
        parameters.Add("p_short_name", entity.ShortName);
        parameters.Add("p_is_positive", entity.IsPositive);

        using var connection = await OpenConnectionAsync(cancellationToken);

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            SqlStatements.ForEventDefinitions.UpsertCustomEventDefinitionWithSortOrder,
            parameters,
            cancellationToken: cancellationToken));

        var created = await multi.ReadFirstOrDefaultAsync<EventDefinition>();
        if (created == null)
        {
            return (null, 0);
        }

        var sortOrder = await multi.ReadSingleOrDefaultAsync<int>();

        return (created, sortOrder);
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(Guid id, string userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_id", id);
        parameters.Add(UserIdParameter, userId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.SoftDeleteEventDefinition,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserEventDefinitionProjection>> GetAvailableForUserAsync(string userId, Guid sportId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add(UserIdParameter, userId);
        parameters.Add("p_sport_id", sportId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<UserEventDefinitionProjection>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.GetUserAvailableEventDefinitions,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserEventDefinitionProjection>> GetMatchEventDefinitionsAsync(Guid matchId, string? userId = null, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);
        parameters.Add(UserIdParameter, userId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<UserEventDefinitionProjection>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.GetMatchEventDefinitions,
            parameters,
            cancellationToken: cancellationToken));
    }
}