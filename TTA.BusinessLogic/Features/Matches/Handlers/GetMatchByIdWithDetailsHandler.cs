using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the retrieval of a single match with its metadata and performs mapping to the response DTO.
/// </summary>
/// <param name="repository">The match repository for data access.</param>
/// <param name="logger">The logger instance for diagnostic messages.</param>
public class GetMatchByIdWithDetailsHandler(
    IMatchRepository repository,
    ILogger<GetMatchByIdWithDetailsHandler> logger) : IRequestHandler<GetMatchByIdWithDetailsQuery, MatchWithDetailsResponse>
{
    private readonly IMatchRepository _repository = repository;
    private readonly ILogger<GetMatchByIdWithDetailsHandler> _logger = logger;

    /// <summary>
    /// Fetches the match details using the repository and maps the dynamic database row to <see cref="MatchWithDetailsResponse"/>.
    /// </summary>
    /// <param name="request">The query containing the match identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A detailed match response object.</returns>
    /// <exception cref="NotFoundException">Thrown if the match with the specified ID does not exist.</exception>
    public async Task<MatchWithDetailsResponse> Handle(GetMatchByIdWithDetailsQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving details for match {MatchId}.", request.Id);

        // Fetching the dynamic result from the storage function public.get_match_with_details_by_id
        var row = await _repository.GetMatchByIdWithDetailsAsync(request.Id, cancellationToken);

        if (row == null)
        {
            _logger.LogWarning("Match with ID {MatchId} was not found.", request.Id);
            throw new NotFoundException($"Match with ID {request.Id} was not found.");
        }

        var response = new MatchWithDetailsResponse(
            Id: row.id,
            TournamentId: row.tournamentid,
            TournamentName: row.tournamentname,
            HomeTeamId: row.hometeamid,
            HomeTeamName: row.hometeamname,
            GuestTeamId: row.guestteamid,
            GuestTeamName: row.guestteamname,
            ScheduledAt: row.scheduledat,
            MatchNumber: row.matchnumber,
            Venue: row.venue,
            Temperature: row.temperature,
            HomeScore: row.homescore,
            GuestScore: row.guestscore,
            CreatedAt: row.createdat
        );

        _logger.LogInformation("Successfully retrieved details for match {MatchNumber} ({MatchId}).",
            response.MatchNumber, request.Id);

        return response;
    }
}