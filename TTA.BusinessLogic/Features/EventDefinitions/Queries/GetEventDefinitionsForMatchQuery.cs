using MediatR;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;

namespace TTA.BusinessLogic.Features.EventDefinitions.Queries;

/// <summary>
/// Query to retrieve active event definitions required for match hydration based on user presets or system default fallback.
/// </summary>
/// <param name="MatchId">The unique identifier of the target match.</param>
/// <param name="UserId">The optional unique identifier of the tracking operator user.</param>
public record GetEventDefinitionsForMatchQuery(Guid MatchId, string? UserId = null) : IRequest<IEnumerable<EventDefinitionResponse>>;