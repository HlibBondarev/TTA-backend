using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles sharing a tracked match with another user using their email address.
/// </summary>
/// <param name="matchRepository">The repository for match data operations.</param>
/// <param name="userRepository">The repository for user retrieval operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class AddUserToTrackedMatchHandler(
    IMatchRepository matchRepository,
    IUserRepository userRepository,
    ILogger<AddUserToTrackedMatchHandler> logger) : IRequestHandler<AddUserToTrackedMatchCommand, bool>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IUserRepository _userRepository = userRepository;
    private readonly ILogger<AddUserToTrackedMatchHandler> _logger = logger;

    /// <summary>
    /// Verifies caller authorization and links the target user to the specified tracked match.
    /// </summary>
    /// <param name="request">The command containing match, team, caller ID, and target email.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task returning true if the target user was linked to the match.</returns>
    /// <exception cref="ConflictException">Thrown when caller does not track the match or DB rules fail.</exception>
    /// <exception cref="NotFoundException">Thrown when target user with specified email is not found.</exception>
    public async Task<bool> Handle(AddUserToTrackedMatchCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("User {CallerId} attempting to share Match {MatchId} (Team {TeamId}) with Email {Email}.",
            request.CurrentUserId, request.MatchId, request.TeamId, request.Email);

        // 1. Verify caller tracks this match/team context
        var callerTracksMatch = await _matchRepository.IsMatchCatchedByUserAsync(
            request.MatchId, request.TeamId, request.CurrentUserId, cancellationToken);

        if (!callerTracksMatch)
        {
            _logger.LogWarning("Share match failed: Caller {CallerId} does not track Match {MatchId} for Team {TeamId}.",
                request.CurrentUserId, request.MatchId, request.TeamId);
            throw new ConflictException("You must be tracking this match before sharing it with another user.");
        }

        // 2. Resolve target user by email
        var targetUsers = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        var targetUser = targetUsers.FirstOrDefault();

        if (targetUser == null)
        {
            _logger.LogWarning("Share match failed: Target user with email {Email} not found.", request.Email);
            throw new NotFoundException($"User with email '{request.Email}' was not found.");
        }

        try
        {
            // 3. Link target user to the match
            var isCatched = await _matchRepository.CatchMatchAsync(
                request.MatchId, request.TeamId, targetUser.Id, cancellationToken);

            _logger.LogInformation("Match {MatchId} successfully shared with User {TargetUserId} ({Email}).",
                request.MatchId, targetUser.Id, request.Email);

            return isCatched;
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Share match failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }
}