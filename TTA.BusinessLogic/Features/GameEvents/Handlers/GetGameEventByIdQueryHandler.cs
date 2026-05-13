using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.GameEvents.Handlers;

/// <summary>
/// Handles the retrieval of a single detailed game event.
/// </summary>
/// <param name="gameEventRepository">The repository for game event data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class GetGameEventByIdQueryHandler(
    IGameEventRepository gameEventRepository,
    ILogger<GetGameEventByIdQueryHandler> logger)
    : IRequestHandler<GetGameEventByIdQuery, GameEventResponse>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly ILogger<GetGameEventByIdQueryHandler> _logger = logger;

    /// <summary>
    /// Retrieves a detailed event record by ID and maps it to the response DTO.
    /// </summary>
    /// <param name="request">The query containing the event identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the detailed game event response.</returns>
    /// <exception cref="NotFoundException">Thrown when the game event with the specified ID does not exist.</exception>
    public async Task<GameEventResponse> Handle(GetGameEventByIdQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching details for GameEvent {Id}.", request.Id);

        var e = await _gameEventRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

        if (e == null)
        {
            _logger.LogWarning("Game event retrieval failed: Event {Id} not found.", request.Id);
            throw new NotFoundException($"Game event with ID {request.Id} not found.");
        }

        var response = new GameEventResponse(
            Id: e.Id,
            MatchLineupId: e.MatchLineupId,
            EventDefinitionId: e.EventDefinitionId,
            EventName: e.EventName,
            IsPositive: e.IsPositive,
            PeriodNumber: e.PeriodNumber,
            EventTimestamp: e.EventTimestamp,
            NormalizedMatchTime: e.NormalizedMatchTime,
            IsLeadToGoal: e.IsLeadToGoal,
            PlayerName: e.PlayerName,
            PlayerNumber: e.PlayerNumber,
            TeamId: e.TeamId,
            TeamName: e.TeamName
        );

        _logger.LogInformation("Successfully retrieved details for GameEvent {Id}.", request.Id);
        return response;
    }
}