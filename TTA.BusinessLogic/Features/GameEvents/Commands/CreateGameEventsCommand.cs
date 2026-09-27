using MediatR;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.GameEvents.Commands;

/// <summary>
/// Direct batch command to persist a collection of game events recorded in a match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match context.</param>
/// <param name="Events">Collection of event requests originating from the client.</param>
public record CreateGameEventsCommand(
    Guid MatchId,
    IEnumerable<CreateGameEventRequest> Events) : IRequest<IEnumerable<Guid>>;

/// <summary>
/// Extensions for mapping game event requests directly to domain entities.
/// </summary>
public static class CreateGameEventsCommandExtensions
{
    /// <summary>
    /// Maps a request DTO to a domain entity preserving client-supplied Id and Timestamp.
    /// </summary>
    public static GameEvent ToModel(this CreateGameEventRequest req) => new()
    {
        Id = req.Id,
        MatchLineupId = req.MatchLineupId,
        EventDefinitionId = req.EventDefinitionId,
        PeriodNumber = req.PeriodNumber,
        EventTimestamp = req.EventTimestamp.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(req.EventTimestamp, DateTimeKind.Utc),
            _ => req.EventTimestamp.ToUniversalTime()
        },
        IsLeadToGoal = req.IsLeadToGoal,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a collection of request DTOs to domain entities.
    /// </summary>
    public static List<GameEvent> ToModel(this IEnumerable<CreateGameEventRequest> requests)
        => [.. requests.Select(ToModel)];
}