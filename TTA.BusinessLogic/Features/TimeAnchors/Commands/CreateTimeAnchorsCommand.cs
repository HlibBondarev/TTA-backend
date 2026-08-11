using MediatR;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.TimeAnchors.Commands;

/// <summary>
/// Direct batch command to persist a collection of time anchors for a match timeline.
/// </summary>
/// <param name="MatchId">The unique identifier of the match context.</param>
/// <param name="Anchors">Collection of time anchor requests originating from the client.</param>
public record CreateTimeAnchorsCommand(
    Guid MatchId,
    IEnumerable<CreateTimeAnchorRequest> Anchors) : IRequest<IEnumerable<Guid>>;

/// <summary>
/// Extensions for mapping time anchor requests directly to domain entities.
/// </summary>
public static class CreateTimeAnchorsCommandExtensions
{
    /// <summary>
    /// Maps a time anchor request DTO to a domain entity preserving client-supplied parameters.
    /// </summary>
    /// <param name="req">The request DTO.</param>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <returns>A mapped <see cref="TimeAnchor"/> domain model.</returns>
    public static TimeAnchor ToModel(this CreateTimeAnchorRequest req, Guid matchId) => new()
    {
        Id = req.Id,
        MatchId = matchId,
        PeriodNumber = req.PeriodNumber,
        Type = req.Type,
        Timestamp = req.Timestamp.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(req.Timestamp, DateTimeKind.Utc),
            _ => req.Timestamp.ToUniversalTime()
        }
    };

    /// <summary>
    /// Maps a collection of time anchor requests to domain entities.
    /// </summary>
    /// <param name="requests">Collection of request DTOs.</param>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <returns>A list of mapped <see cref="TimeAnchor"/> domain models.</returns>
    public static List<TimeAnchor> ToModel(this IEnumerable<CreateTimeAnchorRequest> requests, Guid matchId)
        => requests.Select(r => r.ToModel(matchId)).ToList();
}