using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Base;
using TTA.DataAccess.Repository.Projections;

namespace TTA.DataAccess.Repository.Api;

/// <summary>
/// Defines data access operations for the EventDefinition entity.
/// </summary>
public interface IEventDefinitionRepository : IEntityRepositoryBase<Guid, EventDefinition>
{
    /// <summary>
    /// Upserts a custom user-owned event definition.
    /// </summary>
    /// <param name="entity">The event definition entity to upsert.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The persisted event definition instance.</returns>
    Task<EventDefinition?> UpsertCustomAsync(EventDefinition entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a custom event definition owned by a specific user.
    /// </summary>
    /// <param name="id">The unique identifier of the target event definition.</param>
    /// <param name="userId">The unique identifier of the owner user.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>True if the definition was deleted; otherwise, false.</returns>
    Task<bool> SoftDeleteAsync(Guid id, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all available event definitions (system defaults and user custom ones) for a sport context.
    /// </summary>
    /// <param name="userId">The unique identifier of the target user.</param>
    /// <param name="sportId">The unique identifier of the target sport.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of event definition projections with preset metadata.</returns>
    Task<IEnumerable<UserEventDefinitionProjection>> GetAvailableForUserAsync(string userId, Guid sportId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves active event definitions for the sport associated with a specific match.
    /// </summary>
    /// <param name="matchId">The unique identifier of the target match.</param>
    /// <param name="userId">The optional unique identifier of the operator user.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of active event definition projections for match hydration.</returns>
    Task<IEnumerable<UserEventDefinitionProjection>> GetMatchEventDefinitionsAsync(Guid matchId, string? userId = null, CancellationToken cancellationToken = default);
}