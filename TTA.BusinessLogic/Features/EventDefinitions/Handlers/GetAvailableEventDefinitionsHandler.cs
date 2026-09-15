using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.EventDefinitions.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.EventDefinitions.Handlers;

/// <summary>
/// Handles the retrieval of all available event definitions for a specific user and sport context.
/// </summary>
/// <param name="eventDefinitionRepository" >The repository for event definition data access.</param >
/// <param name="logger">The logger instance for diagnostic information.</param>
public class GetAvailableEventDefinitionsHandler(
    IEventDefinitionRepository eventDefinitionRepository,
    ILogger<GetAvailableEventDefinitionsHandler> logger)
    : IRequestHandler<GetAvailableEventDefinitionsQuery, IEnumerable<EventDefinitionResponse>>
{
    private readonly IEventDefinitionRepository _eventDefinitionRepository = eventDefinitionRepository;
    private readonly ILogger<GetAvailableEventDefinitionsHandler> _logger = logger;

    /// <summary>
    /// Fetches available event definitions for a user and sport context and maps projections to response DTOs.
    /// </summary>
    /// <param name="request">The query containing sport and user identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of event definition responses.</returns>
    public async Task<IEnumerable<EventDefinitionResponse>> Handle(
        GetAvailableEventDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving available event definitions for Sport {SportId} and User {UserId}.", request.SportId, request.UserId);

        var projections = await _eventDefinitionRepository.GetAvailableForUserAsync(request.UserId, request.SportId, cancellationToken);

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

        _logger.LogInformation("Successfully retrieved {Count} available event definitions for Sport {SportId}.", response.Count, request.SportId);

        return response;
    }
}