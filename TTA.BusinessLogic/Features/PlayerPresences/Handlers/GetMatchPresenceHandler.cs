using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.PlayerPresences.Handlers;

/// <summary>
/// Handles the retrieval of all player presence logs for a specific match.
/// Validates the match scope and maps database entities to standardized response DTOs.
/// </summary>
/// <param name="playerPresenceRepository">The specific data access repository for reading player field sessions.</param>
/// <param name="matchRepository">The data access repository for validating targeted match existence status.</param>
/// <param name="logger">The application-scoped diagnostic system component used for debugging context.</param>
public class GetMatchPresenceHandler(
    IPlayerPresenceRepository playerPresenceRepository,
    IMatchRepository matchRepository,
    ILogger<GetMatchPresenceHandler> logger) : IRequestHandler<GetMatchPresenceQuery, IEnumerable<PlayerPresenceResponse>>
{
    private readonly IPlayerPresenceRepository _playerPresenceRepository = playerPresenceRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<GetMatchPresenceHandler> _logger = logger;

    /// <summary>
    /// Processes the query by validating the match scope and fetching all associated presence records.
    /// </summary>
    /// <param name="request">The specialized incoming query descriptor containing the target match parameter.</param>
    /// <param name="cancellationToken">A secure propagation token to monitor for task cancellation requests.</param>
    /// <returns>A mapped collection of player presence response objects ordered chronologically.</returns>
    /// <exception cref="NotFoundException">Thrown if the specified match context identifier fails validation checks entirely.</exception>
    public async Task<IEnumerable<PlayerPresenceResponse>> Handle(GetMatchPresenceQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving player presence history for Match {MatchId}.", request.MatchId);

        // 1. Verify existence of the parent match scope
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null)
        {
            _logger.LogWarning("Query failed: Match {MatchId} not found.", request.MatchId);
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

        // 2. Fetch records using the specialized repository function
        var presences = await _playerPresenceRepository.GetMatchPresenceAsync(request.MatchId, cancellationToken);

        // 3. Map entities to response DTOs
        var response = presences.Select(p => new PlayerPresenceResponse(
            Id: p.Id,
            MatchLineupId: p.MatchLineupId,
            PeriodNumber: p.PeriodNumber,
            TimeIn: p.TimeIn,
            TimeOut: p.TimeOut
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} presence records for Match {MatchId}.", response.Count, request.MatchId);

        return response;
    }
}