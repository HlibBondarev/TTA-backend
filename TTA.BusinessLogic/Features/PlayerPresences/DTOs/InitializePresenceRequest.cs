using TTA.BusinessLogic.Features.PlayerPresences.Commands;

namespace TTA.BusinessLogic.Features.PlayerPresences.DTOs;
/// <summary>
/// Represents an individual player presence item provided by the client.
/// </summary>
/// <param name="Id">The client-generated unique identifier for the presence record.</param>
/// <param name="MatchLineupId">The unique identifier of the player's lineup record.</param>
public record PlayerPresenceItemDto(Guid Id, Guid MatchLineupId);

/// <summary>
/// Data transfer object representing an incoming HTTP request to bulk initialize active player presence at the start of a period.
/// </summary>
/// <param name="PeriodNumber">The specific match period number being started.</param>
/// <param name="TimeIn">The client-side UTC timestamp marking when players entered the field.</param>
/// <param name="PresenceItems">The collection of explicit presence items containing client IDs and lineup IDs.</param>
public record InitializePresenceRequest(
    int PeriodNumber,
    DateTime TimeIn,
    IEnumerable<PlayerPresenceItemDto> PresenceItems);

/// <summary>
/// Provides mapping extension methods for transforming initialization requests into domain commands.
/// </summary>
public static class InitializePresenceRequestExtensions
{
    /// <summary>
    /// Maps the HTTP request payload and route parameters to the internal MediatR initialization command.
    /// </summary>
    /// <param name="request">The incoming data transfer object payload containing presence items.</param>
    /// <param name="matchId">The targeted match unique identifier extracted from the URL route.</param>
    /// <returns>A fully populated domain command ready for MediatR execution.</returns>
    public static InitializePresenceCommand ToCommand(this InitializePresenceRequest request, Guid matchId)
    {
        return new InitializePresenceCommand(
            MatchId: matchId,
            PeriodNumber: request.PeriodNumber,
            TimeIn: request.TimeIn,
            PresenceItems: request.PresenceItems.Select(x => new PlayerPresenceItem(x.Id, x.MatchLineupId))
        );
    }
}