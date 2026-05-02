using MediatR;

namespace TTA.BusinessLogic.Features.MatchLineups.Commands;

/// <summary>
/// Command to batch-copy all players from a specific team's tournament roster into the match lineup.
/// The TournamentId is resolved internally by the system based on the MatchId.
/// </summary>
/// <param name="MatchId">The unique identifier of the match where players will be added.</param>
/// <param name="TeamId">The unique identifier of the team whose roster is being copied.</param>
public record CopyTeamRosterToMatchLineupCommand(
    Guid MatchId,
    Guid TeamId) : IRequest<int>;