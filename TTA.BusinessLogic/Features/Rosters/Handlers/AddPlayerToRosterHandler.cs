using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Rosters.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Rosters.Handlers;

/// <summary>
/// Handles the business logic for adding or updating a player in a tournament roster.
/// Ensures the tournament exists and is still active before persisting changes.
/// </summary>
public class AddPlayerToRosterHandler(
    IRosterRepository rosterRepository,
    ITournamentRepository tournamentRepository,
    ILogger<AddPlayerToRosterHandler> logger) : IRequestHandler<AddPlayerToRosterCommand, Guid>
{
    private readonly IRosterRepository _rosterRepository = rosterRepository;
    private readonly ITournamentRepository _tournamentRepository = tournamentRepository;
    private readonly ILogger<AddPlayerToRosterHandler> _logger = logger;

    /// <summary>
    /// Handles the roster assignment request.
    /// </summary>
    /// <param name="command">The command containing roster assignment details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unique identifier of the created or updated roster entry.</returns>
    /// <exception cref="NotFoundException">Thrown when the specified tournament does not exist.</exception>
    /// <exception cref="BadRequestException">Thrown when attempting to modify a finished tournament.</exception>
    /// <exception cref="ConflictException">Thrown when jersey number or player registration conflicts occur.</exception>
    public async Task<Guid> Handle(AddPlayerToRosterCommand command, CancellationToken cancellationToken)
    {
        // 1. Validate Tournament existence
        var tournament = await _tournamentRepository.GetByIdAsync(command.TournamentId, cancellationToken);
        if (tournament == null)
        {
            _logger.LogWarning("AddRosterItem failed: Tournament {TournamentId} not found.", command.TournamentId);
            throw new NotFoundException($"Tournament with ID {command.TournamentId} was not found.");
        }

        // 2. Validate Tournament timeline
        if (tournament.EndDate < DateTime.UtcNow)
        {
            _logger.LogWarning("AddRosterItem failed: Tournament {TournamentId} has already ended.", command.TournamentId);
            throw new BadRequestException("Cannot modify rosters for a finished tournament.");
        }

        try
        {
            var rosterItem = command.ToModel();
            var result = await _rosterRepository.UpsertRosterItemAsync(rosterItem, cancellationToken);

            _logger.LogInformation("Player {PlayerId} assigned to Team {TeamId} in Tournament {TournamentId}.",
                command.PlayerId, command.TeamId, command.TournamentId);

            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            _logger.LogWarning(ex, "Roster conflict: Jersey number {Number} already exists.", command.Number);
            throw new ConflictException($"Jersey number {command.Number} is already taken in this team roster.");
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Roster violation: Player {PlayerId} already registered in another team.", command.PlayerId);
            throw new ConflictException(ex.Message);
        }
    }
}