using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the retrieval of a player's detailed match report.
/// </summary>
/// <param name="matchRepository">The repository for match data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class GetPlayerDetailedReportHandler(
    IMatchRepository matchRepository,
    ILogger<GetPlayerDetailedReportHandler> logger)
    : IRequestHandler<GetPlayerDetailedReportQuery, PlayerDetailedMatchReportResponse>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<GetPlayerDetailedReportHandler> _logger = logger;

    /// <summary>
    /// Fetches detailed report projections from the repository and aggregates them into a structured player report response.
    /// </summary>
    /// <param name="request">The query containing match and lineup identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The detailed player match report response.</returns>
    /// <exception cref="NotFoundException">Thrown when the lineup record is not found.</exception>
    public async Task<PlayerDetailedMatchReportResponse> Handle(GetPlayerDetailedReportQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching detailed player report for MatchLineup {MatchLineupId} in Match {MatchId}.", request.MatchLineupId, request.MatchId);

        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null || !match.HomeScore.HasValue || !match.GuestScore.HasValue)
        {
            _logger.LogWarning("Detailed player report failed: Match {MatchId} is not finalized or does not exist.", request.MatchId);
            throw new NotFoundException($"Match with ID {request.MatchId} is not finalized or does not exist.");
        }

        var projections = (await _matchRepository.GetPlayerDetailedReportAsync(request.MatchId, request.MatchLineupId, cancellationToken)).ToList();

        if (projections.Count == 0)
        {
            _logger.LogWarning("Detailed player report failed: MatchLineup {MatchLineupId} not found.", request.MatchLineupId);
            throw new NotFoundException($"Match lineup with ID {request.MatchLineupId} not found.");
        }

        var first = projections[0];

        var events = projections
            .Where(p => p.EventId.HasValue)
            .Select(p => new PlayerDetailedEventResponse(
                EventName: p.EventName!,
                IsPositive: p.IsPositive!.Value,
                PeriodNumber: p.PeriodNumber!.Value,
                EventTimestamp: p.EventTimestamp!.Value,
                NormalizedMatchTime: p.NormalizedMatchTime,
                IsLeadToGoal: p.IsLeadToGoal!.Value
            ))
            .OrderBy(e => e.NormalizedMatchTime ?? TimeSpan.MaxValue)
            .ThenBy(e => e.EventTimestamp)
            .ToList();

        var response = new PlayerDetailedMatchReportResponse(
            FirstName: first.FirstName,
            LastName: first.LastName,
            Number: first.Number,
            Events: events
        );

        _logger.LogInformation("Successfully retrieved detailed player report for MatchLineup {MatchLineupId}.", request.MatchLineupId);
        return response;
    }
}