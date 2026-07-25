using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the EventDefinition entity.
/// </summary>
public interface IEventDefinitionRepository : IEntityRepositoryBase<Guid, EventDefinition>
{
    /// <summary>
    /// Retrieves all event definitions for the sport associated with a specific match.
    /// </summary>
    /// <param name="matchId">The unique identifier of the target match.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of event definitions matching the sport context.</returns>
    Task<IEnumerable<EventDefinition>> GetMatchEventDefinitionsAsync(Guid matchId, CancellationToken cancellationToken = default);
}