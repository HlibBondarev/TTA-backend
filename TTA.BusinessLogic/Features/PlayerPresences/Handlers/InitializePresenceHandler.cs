using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.PlayerPresences.Handlers;

/// <summary>
/// Handles the manual/explicit initialization of player presence records at the start of any period.
/// </summary>
/// <param name="playerPresenceRepository">The data access repository interface for player presence tracking tasks.</param>
/// <param name="matchRepository">The database query access gateway for evaluating match validity states.</param>
/// <param name="logger">The infrastructure logging framework object instance used for troubleshooting steps tracking.</param>
public class InitializePresenceHandler(
    IPlayerPresenceRepository playerPresenceRepository,
    IMatchRepository matchRepository,
    ILogger<InitializePresenceHandler> logger) : IRequestHandler<InitializePresenceCommand>
{
    private readonly IPlayerPresenceRepository _playerPresenceRepository = playerPresenceRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<InitializePresenceHandler> _logger = logger;

    /// <summary>
    /// Handles processing for the bulk initial lineup session generation sequence.
    /// </summary>
    /// <param name="request">The specialized incoming command descriptor holding required data payload values.</param>
    /// <param name="cancellationToken">A security token propagation element targeting task cancel flags tracking.</param>
    /// <returns>A task runtime completion confirmation promise wrapper object.</returns>
    /// <exception cref="NotFoundException">Thrown if the targeted parent match identifier fails validation checks entirely.</exception>
    /// <exception cref="ConflictException">Thrown when underlying entity mapping structures or storage definitions throw constraint exceptions.</exception>
    public async Task Handle(InitializePresenceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing presence for {Count} players in Match {MatchId}, Period {Period}.",
            request.PlayerLineupIds.Count(), request.MatchId, request.PeriodNumber);

        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null)
        {
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

        var exactStartTime = DateTime.UtcNow;

        try
        {
            await _playerPresenceRepository.InitializePeriodPresenceAsync(
                request.PeriodNumber,
                exactStartTime,
                request.PlayerLineupIds,
                cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new ConflictException("One or more provided player lineup IDs do not exist in the match protocol.", ex);
        }
    }
}