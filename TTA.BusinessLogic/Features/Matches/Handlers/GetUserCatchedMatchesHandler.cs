using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles retrieving all detailed matches tracked by a specific user.
/// </summary>
/// <param name="matchRepository">The repository for match data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class GetUserCatchedMatchesHandler(
    IMatchRepository matchRepository,
    ILogger<GetUserCatchedMatchesHandler> logger) : IRequestHandler<GetUserCatchedMatchesQuery, IEnumerable<MatchWithDetailsResponse>>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<GetUserCatchedMatchesHandler> _logger = logger;

    /// <summary>
    /// Fetches detailed match projections for the user and maps them to response DTOs.
    /// </summary>
    /// <param name="request">The query containing the user identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of detailed match response DTOs tracked by the user.</returns>
    public async Task<IEnumerable<MatchWithDetailsResponse>> Handle(
        GetUserCatchedMatchesQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching catched matches for User {UserId}.", request.UserId);

        var projections = await _matchRepository.GetCatchedMatchesByUserIdAsync(request.UserId, cancellationToken);

        var response = projections.Select(p => new MatchWithDetailsResponse(
            Id: p.Id,
            TournamentId: p.TournamentId,
            TournamentName: p.TournamentName,
            HomeTeamId: p.HomeTeamId,
            HomeTeamName: p.HomeTeamName,
            GuestTeamId: p.GuestTeamId,
            GuestTeamName: p.GuestTeamName,
            ScheduledAt: p.ScheduledAt,
            MatchNumber: p.MatchNumber,
            Venue: p.Venue,
            Temperature: p.Temperature,
            HomeScore: p.HomeScore,
            GuestScore: p.GuestScore,
            CreatedAt: p.CreatedAt
        ));

        _logger.LogInformation("Successfully retrieved {Count} catched matches for User {UserId}.",
            projections.Count(), request.UserId);

        return response;
    }
}