using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.TimeAnchors.Handlers;

/// <summary>
/// Handles the retrieval of a single time anchor by its identifier.
/// </summary>
/// <param name="timeAnchorRepository">The repository for time anchor data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow.</param>
public class GetTimeAnchorByIdHandler(
    ITimeAnchorRepository timeAnchorRepository,
    ILogger<GetTimeAnchorByIdHandler> logger)
    : IRequestHandler<GetTimeAnchorByIdQuery, TimeAnchorResponse>
{
    private readonly ITimeAnchorRepository _timeAnchorRepository = timeAnchorRepository;
    private readonly ILogger<GetTimeAnchorByIdHandler> _logger = logger;

    /// <summary>
    /// Fetches a time anchor record by ID and maps it to the response DTO.
    /// </summary>
    /// <param name="request">The query containing the anchor identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the time anchor response.</returns>
    /// <exception cref="NotFoundException">Thrown when the time anchor with the specified ID does not exist.</exception>
    public async Task<TimeAnchorResponse> Handle(GetTimeAnchorByIdQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching details for TimeAnchor {Id}.", request.Id);

        // Accessing the repository to find the anchor by its unique identifier
        var anchor = await _timeAnchorRepository.GetByIdAsync(request.Id, cancellationToken);

        if (anchor == null)
        {
            _logger.LogWarning("Time anchor retrieval failed: Anchor {Id} not found.", request.Id);
            throw new NotFoundException($"Time anchor with ID {request.Id} was not found.");
        }

        // Mapping the domain model to the response DTO
        var response = new TimeAnchorResponse(
            Id: anchor.Id,
            MatchId: anchor.MatchId,
            PeriodNumber: anchor.PeriodNumber,
            Type: anchor.Type,
            Timestamp: anchor.Timestamp
        );

        _logger.LogInformation("Successfully retrieved details for TimeAnchor {Id}.", request.Id);

        return response;
    }
}