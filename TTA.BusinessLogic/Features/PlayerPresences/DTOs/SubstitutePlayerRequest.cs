using TTA.BusinessLogic.Features.PlayerPresences.Commands;

namespace TTA.BusinessLogic.Features.PlayerPresences.DTOs;

/// <summary>
/// Data transfer object representing an incoming HTTP request to execute a player substitution.
/// </summary>
/// <param name="PeriodNumber">The current active match period sequence number.</param>
/// <param name="PlayerOutLineupId">The unique lineup protocol identifier of the player leaving the field.</param>
/// <param name="PlayerInLineupId">The unique lineup protocol identifier of the player entering the field.</param>
public record SubstitutePlayerRequest(
    int PeriodNumber,
    Guid PlayerOutLineupId,
    Guid PlayerInLineupId);

/// <summary>
/// Provides mapping extension methods for transforming substitution requests into domain commands.
/// </summary>
public static class SubstitutePlayerRequestExtensions
{
    /// <summary>
    /// Maps the HTTP request payload and route parameters to the internal MediatR substitution command.
    /// </summary>
    /// <param name="request">The incoming data transfer object payload containing substitution details.</param>
    /// <param name="matchId">The targeted match unique identifier extracted from the URL route.</param>
    /// <returns>A fully populated domain command ready for MediatR execution.</returns>
    public static SubstitutePlayerCommand ToCommand(this SubstitutePlayerRequest request, Guid matchId)
    {
        return new SubstitutePlayerCommand(
            MatchId: matchId,
            PeriodNumber: request.PeriodNumber,
            PlayerOutLineupId: request.PlayerOutLineupId,
            PlayerInLineupId: request.PlayerInLineupId
        );
    }
}