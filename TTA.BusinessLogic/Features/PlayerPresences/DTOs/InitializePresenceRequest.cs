using TTA.BusinessLogic.Features.PlayerPresences.Commands;

namespace TTA.BusinessLogic.Features.PlayerPresences.DTOs;

/// <summary>
/// Data transfer object representing an incoming HTTP request to bulk initialize active player presence at the start of a period.
/// </summary>
/// <param name="PeriodNumber">The specific match period number being started.</param>
/// <param name="PlayerLineupIds">The collection of explicit lineup protocol identifiers for the players starting this period.</param>
public record InitializePresenceRequest(
    int PeriodNumber,
    IEnumerable<Guid> PlayerLineupIds);

/// <summary>
/// Provides mapping extension methods for transforming initialization requests into domain commands.
/// </summary>
public static class InitializePresenceRequestExtensions
{
    /// <summary>
    /// Maps the HTTP request payload and route parameters to the internal MediatR initialization command.
    /// </summary>
    /// <param name="request">The incoming data transfer object payload containing lineup array details.</param>
    /// <param name="matchId">The targeted match unique identifier extracted from the URL route.</param>
    /// <returns>A fully populated domain command ready for MediatR execution.</returns>
    public static InitializePresenceCommand ToCommand(this InitializePresenceRequest request, Guid matchId)
    {
        return new InitializePresenceCommand(
            MatchId: matchId,
            PeriodNumber: request.PeriodNumber,
            PlayerLineupIds: request.PlayerLineupIds
        );
    }
}