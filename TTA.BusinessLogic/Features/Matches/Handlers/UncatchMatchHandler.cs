using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the removal of a tracking link between a user and a match/team context.
/// </summary>
/// <param name="matchRepository">The repository for match data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class UncatchMatchHandler(
    IMatchRepository matchRepository,
    ILogger<UncatchMatchHandler> logger) : IRequestHandler<UncatchMatchCommand, bool>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<UncatchMatchHandler> _logger = logger;

    /// <summary>
    /// Verifies tracking existence and removes the link for the user.
    /// </summary>
    /// <param name="request">The command containing match, team, and user identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task returning true if the tracking link was deleted successfully.</returns>
    /// <exception cref="NotFoundException">Thrown when the tracking relationship does not exist.</exception>
    public async Task<bool> Handle(UncatchMatchCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("User {UserId} attempting to uncatch Match {MatchId} for Team {TeamId}.",
            request.UserId, request.MatchId, request.TeamId);

        var isTracked = await _matchRepository.IsMatchCatchedByUserAsync(
            request.MatchId, request.TeamId, request.UserId, cancellationToken);

        if (!isTracked)
        {
            _logger.LogWarning("Uncatch match failed: User {UserId} does not track Match {MatchId} for Team {TeamId}.",
                request.UserId, request.MatchId, request.TeamId);
            throw new NotFoundException($"Tracking record for Match {request.MatchId} and Team {request.TeamId} was not found.");
        }

        var isUncatched = await _matchRepository.UncatchMatchAsync(
            request.MatchId, request.TeamId, request.UserId, cancellationToken);

        _logger.LogInformation("Match {MatchId} successfully uncatched for User {UserId}.", request.MatchId, request.UserId);
        return isUncatched;
    }
}