using TTA.BusinessLogic.Features.MatchLineups.Commands;

namespace TTA.BusinessLogic.Features.MatchLineups.DTOs;

/// <summary>
/// Request DTO for bulk copying selected players from a tournament roster to a match protocol.
/// </summary>
/// <param name="PlayerRosterIds">The list of player roster identifiers to be included in the match.</param>
public record CopyTeamRosterToMatchLineupRequest(IEnumerable<Guid> PlayerRosterIds);

/// <summary>
/// Mapping extensions for <see cref="CopyTeamRosterToMatchLineupRequest"/>.
/// </summary>
public static class CopyTeamRosterToMatchLineupRequestExtensions
{
    /// <summary>
    /// Converts a request DTO to a copy command.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="matchId">The identifier of the target match.</param>
    /// <param name="teamId">The identifier of the team.</param>
    /// <returns>A configured <see cref="CopyTeamRosterToMatchLineupCommand"/>.</returns>
    public static CopyTeamRosterToMatchLineupCommand ToCommand(
        this CopyTeamRosterToMatchLineupRequest request,
        Guid matchId,
        Guid teamId) => new(
            MatchId: matchId,
            TeamId: teamId,
            PlayerRosterIds: request.PlayerRosterIds);
}