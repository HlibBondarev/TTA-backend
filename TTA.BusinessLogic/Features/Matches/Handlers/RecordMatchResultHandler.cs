using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the recording of match results.
/// </summary>
public class RecordMatchResultHandler(
    IMatchRepository matchRepository,
    ILogger<RecordMatchResultHandler> logger) : IRequestHandler<RecordMatchResultCommand, Guid>
{
    private readonly IMatchRepository _repository = matchRepository;
    private readonly ILogger<RecordMatchResultHandler> _logger = logger;

    /// <summary>
    /// Processes the command to update match results and metadata.
    /// </summary>
    /// <param name="command">The command containing results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the updated <see cref="Match"/> entity.</returns>
    /// <exception cref="NotFoundException">Thrown if the match does not exist.</exception>
    public async Task<Guid> Handle(RecordMatchResultCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording results for match {MatchId}: {HomeScore}-{GuestScore}",
            command.MatchId, command.HomeScore, command.GuestScore);

        // 1. Retrieve existing match
        var match = await _repository.GetByIdAsync(command.MatchId, cancellationToken);
        if (match == null)
        {
            _logger.LogWarning("Match {MatchId} not found for result recording.", command.MatchId);
            throw new NotFoundException($"Match with ID {command.MatchId} was not found.");
        }

        // 2. Update properties
        command.SetToModel(match);

        // 3. Persist changes via upsert
        var result = await _repository.UpsertMatchAsync(match, cancellationToken);
        _logger.LogInformation("Successfully updated match {MatchNumber} with ID = {MatchId} in tournament {TournamentId}", result.MatchNumber, result.Id, result.TournamentId);

        return result.Id;
    }
}