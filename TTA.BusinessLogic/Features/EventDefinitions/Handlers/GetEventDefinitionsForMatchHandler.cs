using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.EventDefinitions.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.EventDefinitions.Handlers;

/// <summary>
/// Handles the retrieval of active game event definitions for a specific match context.
/// </summary>
/// <param name="eventDefinitionRepository">The repository for event definition data access.</param>
/// <param name="logger">The logger instance for tracking execution.</param>
public class GetEventDefinitionsForMatchHandler(
    IEventDefinitionRepository eventDefinitionRepository,
    ILogger<GetEventDefinitionsForMatchHandler> logger)
    : IRequestHandler<GetEventDefinitionsForMatchQuery, IEnumerable<EventDefinitionResponse>>
{
    private readonly IEventDefinitionRepository _eventDefinitionRepository = eventDefinitionRepository;
    private readonly ILogger<GetEventDefinitionsForMatchHandler> _logger = logger;

    /// <summary>
    /// Fetches active event definitions for a match context and maps projections to response DTOs.
    /// </summary>
    /// <param name="request">The query containing the match identifier and optional user identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of event definition responses.</returns>
    public async Task<IEnumerable<EventDefinitionResponse>> Handle(
        GetEventDefinitionsForMatchQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching event definitions for Match {MatchId}.", request.MatchId);

        var projections = await _eventDefinitionRepository.GetMatchEventDefinitionsAsync(request.MatchId, request.UserId, cancellationToken);

        var response = projections.Select(p => new EventDefinitionResponse(
            Id: p.Id,
            SportId: p.SportId,
            Name: p.Name,
            ShortName: p.ShortName,
            IsPositive: p.IsPositive,
            IsCustom: p.IsCustom,
            IsEnabled: p.IsEnabled,
            SortOrder: p.SortOrder
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} event definitions for Match {MatchId}.", response.Count, request.MatchId);

        return response;
    }
}