using MediatR;

namespace TTA.BusinessLogic.Features.MatchLineups.Commands;

/// <summary>
/// Command to copy selected players from a team's tournament roster to a specific match protocol.
/// </summary>
/// <param name="MatchId">Target match identifier.</param>
/// <param name="TeamId">Team identifier.</param>
/// <param name="PlayerRosterIds">Collection of specific player roster IDs to copy.</param>
public record CopyTeamRosterToMatchLineupCommand(
    Guid MatchId,
    Guid TeamId,
    IEnumerable<Guid> PlayerRosterIds) : IRequest<int>;