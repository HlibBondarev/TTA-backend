using MediatR;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;

namespace TTA.BusinessLogic.Features.EventDefinitions.Queries;

/// <summary>
/// Query to retrieve all available active event definitions for a user and sport with preset state metadata.
/// </summary>
/// <param name="SportId">The unique identifier of the target sport.</param>
/// <param name="UserId">The unique identifier of the target user.</param>
public record GetAvailableEventDefinitionsQuery(Guid SportId, string UserId) : IRequest<IEnumerable<EventDefinitionResponse>>;