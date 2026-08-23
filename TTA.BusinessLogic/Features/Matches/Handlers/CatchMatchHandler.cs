using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the operation of linking a user to a specific match and team context.
/// </summary>
/// <param name="matchRepository">The repository for match data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class CatchMatchHandler(
    IMatchRepository matchRepository,
    ILogger<CatchMatchHandler> logger) : IRequestHandler<CatchMatchCommand, bool>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<CatchMatchHandler> _logger = logger;

    /// <summary>
    /// Validates match existence and establishes a tracking relationship for the user.
    /// </summary>
    /// <param name="request">The command containing match, team, and user identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task returning true if the tracking link was newly created; false if it already existed.</returns>
    /// <exception cref="NotFoundException">Thrown when the target match does not exist.</exception>
    /// <exception cref="ConflictException">Thrown when the team does not participate in the match.</exception>
    public async Task<bool> Handle(CatchMatchCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("User {UserId} attempting to catch Match {MatchId} for Team {TeamId}.",
            request.UserId, request.MatchId, request.TeamId);

        var existingMatch = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (existingMatch == null)
        {
            _logger.LogWarning("Catch match failed: Match {MatchId} not found.", request.MatchId);
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

        try
        {
            var isCatched = await _matchRepository.CatchMatchAsync(
                request.MatchId, request.TeamId, request.UserId, cancellationToken);

            if (isCatched)
            {
                _logger.LogInformation("Match {MatchId} successfully catched by User {UserId} for Team {TeamId}.",
                    request.MatchId, request.UserId, request.TeamId);
            }
            else
            {
                _logger.LogInformation("Match {MatchId} was already catched by User {UserId} for Team {TeamId}.",
                    request.MatchId, request.UserId, request.TeamId);
            }

            return isCatched;
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Catch match failed due to database business rule: {Message}", ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }
}