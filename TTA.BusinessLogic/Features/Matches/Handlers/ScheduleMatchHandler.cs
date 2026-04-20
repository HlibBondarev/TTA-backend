using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Tournaments.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the creation of a match record.
/// </summary>
public class ScheduleMatchHandler(
    IMatchRepository matchRepository,
    ITournamentRepository tournamentRepository,
    ILogger<ScheduleMatchHandler> logger) : IRequestHandler<ScheduleMatchCommand, Guid>
{
    private readonly IMatchRepository _repository = matchRepository;
    private readonly ITournamentRepository _tournamentRepository = tournamentRepository;
    private readonly ILogger<ScheduleMatchHandler> _logger = logger;

    /// <summary>
    /// Processes the tournament creation command.
    /// Maps the command to a domain model and persists it via the repository.
    /// </summary>
    /// <param name="command">The command containing match details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the newly created match.</returns>
    /// <exception cref="ConflictException">Thrown when business rules (e.g., date range) are violated.</exception>
    /// <exception cref="NotFoundException">Thrown when related entities (Tournament, Teams, etc.) do not exist.</exception>
    public async Task<Guid> Handle(ScheduleMatchCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to schedule match {MatchNumber} for tournament {TournamentId}",
            command.MatchNumber, command.TournamentId);

        // 1. Verify that the tournament exists. 
        // This is a missing-resource error (404).
        var tournament = await _tournamentRepository.GetByIdAsync(command.TournamentId, cancellationToken);
        if (tournament == null)
        {
            _logger.LogWarning("ScheduleMatch failed: Tournament {TournamentId} not found.", command.TournamentId);
            throw new NotFoundException($"Tournament with ID {command.TournamentId} was not found.");
        }

        // 2. Validate that ScheduledAt is within the tournament's active dates.
        // Comparison is done using .Date to allow matches in single-day tournaments.
        if (command.ScheduledAt.Date < tournament.StartDate.Date ||
            (tournament.EndDate.HasValue && command.ScheduledAt.Date > tournament.EndDate.Value.Date))
        {
            _logger.LogWarning("Creation rejected: ScheduledAt {ScheduledAt} is outside the tournament date range for tournament {TournamentId}.",
                command.ScheduledAt, command.TournamentId);

            throw new ConflictException("Scheduled date must be within the tournament's active dates.");
        }

        // TODO: Additional validation can be added here - check if HomeTeamId and GuestTeamId are valid and registered for the tournament.

        // 3. Log the creation attempt
        _logger.LogInformation("Processing creation for match: {MatchNumber} in tournament {TournamentId}",
            command.MatchNumber, command.TournamentId);

        // 4. Map to domain model
        var match = command.ToModel();
        try
        {
            // 5. Persistence via repository
            var result = await _repository.UpsertMatchAsync(match, cancellationToken);
            _logger.LogInformation("Successfully created match {MatchNumber} with ID = {MatchId} in tournament {TournamentId}", result.MatchNumber, result.Id, result.TournamentId);

            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            _logger.LogWarning(ex, "Match creation failed: Related entity not found for {MatchNumber}", command.MatchNumber);
            throw new NotFoundException("Tournament or related entities do not exist.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Match creation failed: home or guest team is not registered for this tournament {TournamentId}", command.TournamentId);
            throw new ConflictException("Home team or guest team is not registered for this tournament", ex);
        }
    }
}