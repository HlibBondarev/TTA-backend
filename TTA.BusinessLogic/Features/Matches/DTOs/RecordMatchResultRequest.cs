using TTA.BusinessLogic.Features.Matches.Commands;

namespace TTA.BusinessLogic.Features.Matches.DTOs;


/// <summary>
/// Data transfer object for recording match scores and environmental conditions.
/// </summary>
/// <param name="HomeScore">The number of goals/points scored by the home team.</param>
/// <param name="GuestScore">The number of goals/points scored by the guest team.</param>
/// <param name="Temperature">The recorded ambient temperature during the match (optional).</param>
public record RecordMatchResultRequest(
    int HomeScore,
    int GuestScore,
    double? Temperature);

/// <summary>
/// Extension methods for mapping match-related requests to commands.
/// </summary>
public static class MatchMappingExtensions
{
    /// <summary>
    /// Converts a <see cref="RecordMatchResultRequest"/> to a <see cref="RecordMatchResultCommand"/>.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <returns>A command populated with match results.</returns>
    public static RecordMatchResultCommand ToCommand(this RecordMatchResultRequest request, Guid matchId)
    {
        return new RecordMatchResultCommand(
            MatchId: matchId,
            HomeScore: request.HomeScore,
            GuestScore: request.GuestScore,
            Temperature: request.Temperature);
    }
}