using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the retrieval and mapping of tournament matches.
/// </summary>
/// <param name="repository">The match repository for data access.</param>
/// <param name="logger">The logger instance for diagnostic messages.</param>
public class GetTournamentMatchesHandler(
    IMatchRepository repository,
    ILogger<GetTournamentMatchesHandler> logger) : IRequestHandler<GetTournamentMatchesQuery, IEnumerable<MatchWithDetailsResponse>>
{
    private readonly IMatchRepository _repository = repository;
    private readonly ILogger<GetTournamentMatchesHandler> _logger = logger;

    /// <summary>
    /// Processes the query to fetch matches from the database and map them to structured response DTOs.
    /// </summary>
    /// <param name="request">The query containing the tournament identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of <see cref="MatchWithDetailsResponse"/> objects.</returns>
    public async Task<IEnumerable<MatchWithDetailsResponse>> Handle(GetTournamentMatchesQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving all matches for tournament {TournamentId}.", request.TournamentId);

        // Fetching dynamic results from the storage function public.get_tournament_matches
        var rawMatches = await _repository.GetByTournamentIdAsync(request.TournamentId, cancellationToken);

        var response = rawMatches.Select(row => new MatchWithDetailsResponse(
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
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} matches for tournament {TournamentId}.",
            response.Count, request.TournamentId);

        return response;
    }
}