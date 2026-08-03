using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.SportConfigurations.DTOs;
using TTA.BusinessLogic.Features.SportConfigurations.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.SportConfigurations.Handlers;

/// <summary>
/// Handler for processing <see cref="GetSportConfigurationsBySportIdQuery"/> requests.
/// </summary>
/// <param name="sportConfigurationRepository">The repository instance for accessing sport configuration database records.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
public class GetSportConfigurationsBySportIdHandler(
    ISportConfigurationRepository sportConfigurationRepository,
    ILogger<GetSportConfigurationsBySportIdHandler> logger)
    : IRequestHandler<GetSportConfigurationsBySportIdQuery, IEnumerable<SportConfigurationResponse>>
{
    private readonly ISportConfigurationRepository _sportConfigurationRepository = sportConfigurationRepository;
    private readonly ILogger<GetSportConfigurationsBySportIdHandler> _logger = logger;

    /// <summary>
    /// Handles the execution of the query to retrieve sport configurations by sport ID.
    /// </summary>
    /// <param name="request">The query request payload containing the target sport identifier.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A collection of <see cref="SportConfigurationResponse"/> records.</returns>
    /// <exception cref="NotFoundException">Thrown when no configurations exist for the provided sport ID.</exception>
    public async Task<IEnumerable<SportConfigurationResponse>> Handle(GetSportConfigurationsBySportIdQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving configurations for sport {SportId}.", request.SportId);

        var configs = await _sportConfigurationRepository.GetBySportIdAsync(request.SportId, cancellationToken);

        if (!configs.Any())
        {
            _logger.LogWarning("Configurations retrieval failed: Sport {SportId} not found.", request.SportId);
            throw new NotFoundException($"Sport with ID '{request.SportId}' was not found.");
        }

        return configs.Select(c => new SportConfigurationResponse(
            c.Id,
            c.SportId,
            c.UsesCleanTime,
            c.PeriodsCount,
            c.PeriodDurationMinutes,
            c.FieldSize,
            c.RosterLimit,
            c.LineupLimit,
            c.ActivePlayersLimit));
    }
}