using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the retrieval of the team summary match report.
/// </summary>
/// <param name="matchRepository">The repository for match data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class GetTeamSummaryReportHandler(
    IMatchRepository matchRepository,
    ILogger<GetTeamSummaryReportHandler> logger)
    : IRequestHandler<GetTeamSummaryReportQuery, IEnumerable<TeamMatchSummaryReportResponse>>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<GetTeamSummaryReportHandler> _logger = logger;

    /// <summary>
    /// Fetches team summary report projections from the repository and maps them to response DTOs.
    /// </summary>
    /// <param name="request">The query containing match and team identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of team summary report responses.</returns>
    /// <exception cref="NotFoundException">Thrown when no summary records are found for the specified match and team.</exception>
    public async Task<IEnumerable<TeamMatchSummaryReportResponse>> Handle(GetTeamSummaryReportQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching team summary report for Team {TeamId} in Match {MatchId}.", request.TeamId, request.MatchId);

        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null || !match.HomeScore.HasValue || !match.GuestScore.HasValue)
        {
            _logger.LogWarning("Team summary report failed: Match {MatchId} is not finalized or does not exist.", request.MatchId);
            throw new NotFoundException($"Match with ID {request.MatchId} is not finalized or does not exist.");
        }

        var projections = (await _matchRepository.GetTeamSummaryReportAsync(request.MatchId, request.TeamId, cancellationToken)).ToList();

        if (projections.Count == 0)
        {
            _logger.LogWarning("Team summary report failed: Team {TeamId} or Match {MatchId} not found.", request.TeamId, request.MatchId);
            throw new NotFoundException($"Team {request.TeamId} or Match {request.MatchId} not found.");
        }

        return projections.Select(p => new TeamMatchSummaryReportResponse(
            MatchLineupId: p.MatchLineupId,
            FirstName: p.FirstName,
            LastName: p.LastName,
            Number: p.Number,
            Goals: p.Goals,
            PositiveGoalLeadingActions: p.PositiveGoalLeadingActions,
            NegativeGoalLeadingActions: p.NegativeGoalLeadingActions,
            TotalPositiveActions: p.TotalPositiveActions,
            TotalNegativeActions: p.TotalNegativeActions,
            PlayPercentage: p.PlayPercentage
        ));
    }
}