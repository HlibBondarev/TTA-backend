using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the bulk copying of players from a team's tournament roster to a specific match lineup.
/// </summary>
public class CopyTeamRosterToMatchLineupHandler(
    IMatchLineupRepository matchLineupRepository,
    IMatchRepository matchRepository,
    ILogger<CopyTeamRosterToMatchLineupHandler> logger) : IRequestHandler<CopyTeamRosterToMatchLineupCommand, int>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<CopyTeamRosterToMatchLineupHandler> _logger = logger;

    /// <summary>
    /// Processes the command to copy all registered players of a team into the match protocol.
    /// Validates the existence of the match and delegates the batch operation to the repository.
    /// </summary>
    /// <param name="request">Command containing MatchId and TeamId.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of players successfully added to the match lineup.</returns>
    /// <exception cref="NotFoundException">Thrown if the target match is not found.</exception>
    public async Task<int> Handle(CopyTeamRosterToMatchLineupCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting batch copy of roster for Team {TeamId} into Match {MatchId}.",
            request.TeamId, request.MatchId);

        // 1. Validate that the match exists prior to copying
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null)
        {
            _logger.LogWarning("Batch copy failed: Match {MatchId} not found.", request.MatchId);
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

        // 2. Execute the batch operation via repository with the specific selection of players
        int insertedCount = await _matchLineupRepository.CopyFromRosterAsync(
            request.MatchId,
            request.TeamId,
            request.PlayerRosterIds,
            cancellationToken);

        _logger.LogInformation("Successfully added {Count} players to the lineup for Match {MatchId}.",
            insertedCount, request.MatchId);

        return insertedCount;
    }
}