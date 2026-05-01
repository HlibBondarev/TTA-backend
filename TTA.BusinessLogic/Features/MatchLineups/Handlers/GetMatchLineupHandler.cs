using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the retrieval of the match protocol (lineup).
/// Maps dynamic database results to structured <see cref="MatchLineupResponse"/> DTOs.
/// </summary>
public class GetMatchLineupHandler(
    IMatchLineupRepository matchLineupRepository,
    IMatchRepository matchRepository,
    ILogger<GetMatchLineupHandler> logger) : IRequestHandler<GetMatchLineupQuery, IEnumerable<MatchLineupResponse>>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<GetMatchLineupHandler> _logger = logger;

    /// <summary>
    /// Processes the query to fetch and map the match lineup data.
    /// </summary>
    /// <param name="request">The query containing the match identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of <see cref="MatchLineupResponse"/> objects representing the match protocol.</returns>
    /// <exception cref="NotFoundException">Thrown when the match with the specified identifier was not found.</exception>
    public async Task<IEnumerable<MatchLineupResponse>> Handle(GetMatchLineupQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving protocol for Match {MatchId}.", request.MatchId);

        // 1. Validate Match existence
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null)
        {
            _logger.LogWarning("GetMatchLineup failed: Match {MatchId} not found.", request.MatchId);
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

        // 2. Fetch dynamic data from repository
        var rawLineup = await _matchLineupRepository.GetByMatchIdAsync(request.MatchId, cancellationToken);

        // 3. Map dynamic rows to structured DTOs
        var response = rawLineup.Select(row => new MatchLineupResponse(
            Id: row.id,
            MatchId: row.matchid,
            PlayerRosterId: row.playerrosterid,
            TeamId: row.teamid,
            FirstName: row.firstname,
            LastName: row.lastname,
            Number: row.number,
            IsInStartingLineup: row.isinstartinglineup,
            PositionId: row.positionid,
            PositionName: row.positionname
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} protocol entries for Match {MatchId}.",
            response.Count, request.MatchId);

        return response;
    }
}