using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the retrieval of a single enriched match lineup record.
/// Maps the database results to a <see cref="MatchLineupResponse"/>.
/// </summary>
public class GetMatchLineupByIdHandler(
    IMatchLineupRepository matchLineupRepository,
    ILogger<GetMatchLineupByIdHandler> logger) : IRequestHandler<GetMatchLineupByIdQuery, MatchLineupResponse>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<GetMatchLineupByIdHandler> _logger = logger;

    /// <summary>
    /// Processes the query to fetch detailed lineup information.
    /// </summary>
    /// <param name="request">The query containing the target ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A detailed <see cref="MatchLineupResponse"/>.</returns>
    /// <exception cref="NotFoundException">Thrown when the lineup entry does not exist.</exception>
    public async Task<MatchLineupResponse> Handle(GetMatchLineupByIdQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving enriched lineup details for ID {Id}.", request.Id);

        // Fetch detailed data from the repository (via public.get_match_lineup_details_by_id)
        var result = await _matchLineupRepository.GetMatchLineupByIdWithDetailsAsync(request.Id, cancellationToken);

        if (result == null)
        {
            _logger.LogWarning("Lineup details for ID {Id} not found.", request.Id);
            throw new NotFoundException($"Match lineup entry with ID {request.Id} was not found.");
        }

        // Map the flat result (dynamic or tuple from repository) to the Response DTO
        return new MatchLineupResponse(
            Id: result.id,
            MatchId: result.matchid,
            PlayerRosterId: result.playerrosterid,
            TeamId: result.teamid,
            FirstName: result.firstname,
            LastName: result.lastname,
            Number: result.number,
            IsInStartingLineup: result.isinstartinglineup,
            PositionId: result.positionid,
            PositionName: result.positionname
        );
    }
}