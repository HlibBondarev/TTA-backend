using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Rosters.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Rosters.Handlers;

/// <summary>
/// Handles the business logic for removing a player from a tournament roster.
/// Verifies tournament status and ownership before execution.
/// </summary>
public class RemovePlayerFromRosterHandler(
    IRosterRepository rosterRepository,
    ITournamentRepository tournamentRepository,
    ILogger<RemovePlayerFromRosterHandler> logger) : IRequestHandler<RemovePlayerFromRosterCommand>
{
    private readonly IRosterRepository _rosterRepository = rosterRepository;
    private readonly ITournamentRepository _tournamentRepository = tournamentRepository;
    private readonly ILogger<RemovePlayerFromRosterHandler> _logger = logger;

    /// <summary>
    /// Processes the removal request. Ensures the tournament has not yet ended to maintain data integrity.
    /// </summary>
    /// <param name="request">The command containing tournament and player identifiers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <exception cref="NotFoundException">Thrown if the tournament does not exist.</exception>
    /// <exception cref="BadRequestException">Thrown if the tournament timeline is already closed.</exception>
    public async Task Handle(RemovePlayerFromRosterCommand request, CancellationToken ct)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(request.TournamentId, ct);
        if (tournament == null)
        {
            _logger.LogWarning("RemoveRosterItem failed: Tournament {TournamentId} not found.", request.TournamentId);
            throw new NotFoundException($"Tournament with ID {request.TournamentId} was not found.");
        }

        if (tournament.EndDate < DateTime.UtcNow)
        {
            _logger.LogWarning("RemoveRosterItem failed: Tournament {TournamentId} has ended.", request.TournamentId);
            throw new BadRequestException("Cannot modify rosters of a finished tournament.");
        }

        await _rosterRepository.RemovePlayerFromRosterAsync(request.TournamentId, request.TeamId, request.PlayerId, ct);

        _logger.LogInformation("Player {PlayerId} successfully removed from Tournament {TournamentId}.",
            request.PlayerId, request.TournamentId);
    }
}