using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;


/// <summary>
/// Implements data access operations for event definitions using PostgreSQL storage functions.
/// </summary>
/// <param name="connectionFactory">The factory used to create database connections.</param>
public class EventDefinitionRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, EventDefinition>(connectionFactory), IEventDefinitionRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<EventDefinition>> GetMatchEventDefinitionsAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_match_id", matchId);

        using var connection = await OpenConnectionAsync(cancellationToken);

        return await connection.QueryAsync<EventDefinition>(new CommandDefinition(
            SqlStatements.ForEventDefinitions.GetMatchEventDefinitions,
            parameters,
            cancellationToken: cancellationToken));
    }
}