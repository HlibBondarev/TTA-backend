using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Sports.DTOs;
using TTA.BusinessLogic.Features.Sports.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Sports.Handlers;

/// <summary>
/// Handler for processing <see cref="GetAllSportsQuery"/> requests.
/// </summary>
/// <param name="sportRepository">The repository instance for accessing sport database records.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
public class GetAllSportsHandler(
    ISportRepository sportRepository,
    ILogger<GetAllSportsHandler> logger)
    : IRequestHandler<GetAllSportsQuery, IEnumerable<SportResponse>>
{
    private readonly ISportRepository _sportRepository = sportRepository;
    private readonly ILogger<GetAllSportsHandler> _logger = logger;

    /// <summary>
    /// Handles the execution of the query to retrieve all sports.
    /// </summary>
    /// <param name="request">The query request payload.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A collection of <see cref="SportResponse"/> records.</returns>
    public async Task<IEnumerable<SportResponse>> Handle(GetAllSportsQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving all sports from the database.");
        var sports = await _sportRepository.GetAllSportsAsync(cancellationToken);

        return sports.Select(s => new SportResponse(
            s.Id,
            s.Name,
            s.ShortName,
            s.DefaultConfigId));
    }
}