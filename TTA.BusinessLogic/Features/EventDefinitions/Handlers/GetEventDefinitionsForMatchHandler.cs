using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.EventDefinitions.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.EventDefinitions.Handlers;

/// <summary>
/// Handles the retrieval of game event definitions for a specific match's sport context.
/// </summary>
/// <param name="eventDefinitionRepository">The repository for event definition data access.</param>
/// <param name="logger">The logger instance for tracking execution.</param>
public class GetEventDefinitionsForMatchHandler(
    IEventDefinitionRepository eventDefinitionRepository,
    ILogger<GetEventDefinitionsForMatchHandler> logger)
    : IRequestHandler<GetEventDefinitionsForMatchQuery, IEnumerable<EventDefinitionForMatchResponse>>
{
    private readonly IEventDefinitionRepository _eventDefinitionRepository = eventDefinitionRepository;
    private readonly ILogger<GetEventDefinitionsForMatchHandler> _logger = logger;

    /// <summary>
    /// Fetches event definitions for a match and maps domain models to response DTOs.
    /// </summary>
    /// <param name="request">The query containing the match identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of event definition responses.</returns>
    public async Task<IEnumerable<EventDefinitionForMatchResponse>> Handle(
        GetEventDefinitionsForMatchQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching event definitions for Match {MatchId}.", request.MatchId);

        var definitions = await _eventDefinitionRepository.GetMatchEventDefinitionsAsync(request.MatchId, cancellationToken);

        var response = definitions.Select(d => new EventDefinitionForMatchResponse(
            Id: d.Id,
            SportId: d.SportId,
            Name: d.Name,
            ShortName: d.ShortName,
            IsPositive: d.IsPositive,
            CreatedAt: d.CreatedAt
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} event definitions for Match {MatchId}.", response.Count, request.MatchId);

        return response;
    }
}