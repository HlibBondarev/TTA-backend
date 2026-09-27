using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.TimeAnchors.Handlers;

/// <summary>
/// Handles the retrieval of all time anchors for a specific match.
/// </summary>
/// <param name="timeAnchorRepository">The repository for time anchor data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class GetMatchAnchorsHandler(
    ITimeAnchorRepository timeAnchorRepository,
    ILogger<GetMatchAnchorsHandler> logger)
    : IRequestHandler<GetMatchAnchorsQuery, IEnumerable<TimeAnchorResponse>>
{
    private readonly ITimeAnchorRepository _timeAnchorRepository = timeAnchorRepository;
    private readonly ILogger<GetMatchAnchorsHandler> _logger = logger;

    /// <summary>
    /// Fetches time anchors for a match and maps them to response DTOs.
    /// Sorting is applied chronologically based on the UTC timestamp.
    /// </summary>
    /// <param name="request">The query containing the match identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of time anchor responses ordered by time.</returns>
    public async Task<IEnumerable<TimeAnchorResponse>> Handle(GetMatchAnchorsQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching time anchors for Match {MatchId}.", request.MatchId);

        // Using the correct repository method as defined in ITimeAnchorRepository
        var anchors = await _timeAnchorRepository.GetMatchAnchorsAsync(request.MatchId, cancellationToken);

        // Sorting by Timestamp is sufficient as it represents the absolute timeline of the match
        var response = anchors
            .OrderBy(a => a.Timestamp)
            .Select(a => new TimeAnchorResponse(
                Id: a.Id,
                MatchId: a.MatchId,
                PeriodNumber: a.PeriodNumber,
                Type: a.Type,
                Timestamp: a.Timestamp
            )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} time anchors for Match {MatchId}.", response.Count, request.MatchId);

        return response;
    }
}