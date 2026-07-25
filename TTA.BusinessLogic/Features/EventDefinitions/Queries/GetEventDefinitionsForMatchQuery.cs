using MediatR;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;

namespace TTA.BusinessLogic.Features.EventDefinitions.Queries;

/// <summary>
/// Query to retrieve all event definitions associated with the sport configuration of a specific match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
public record GetEventDefinitionsForMatchQuery(Guid MatchId) : IRequest<IEnumerable<EventDefinitionForMatchResponse>>;