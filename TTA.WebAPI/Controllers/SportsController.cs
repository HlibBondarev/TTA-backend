using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.SportConfigurations.DTOs;
using TTA.BusinessLogic.Features.SportConfigurations.Queries;
using TTA.BusinessLogic.Features.Sports.DTOs;
using TTA.BusinessLogic.Features.Sports.Queries;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing and retrieving sport entities and their related configurations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SportsController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching queries.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SportsController(
    IMediator mediator,
    ILogger<SportsController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<SportsController> _logger = logger;

    /// <summary>
    /// Retrieves a list of all available sports in the system.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of sports.</returns>
    /// <response code="200">Returns the list of sports.</response>
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<SportResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllSports(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetAllSports action.");
        var result = await _mediator.Send(new GetAllSportsQuery(), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Retrieves all configuration profiles for a specific sport.
    /// </summary>
    /// <param name="sportId">The unique identifier of the target sport.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of sport configurations associated with the specified sport.</returns>
    /// <response code="200">Returns the list of sport configurations.</response>
    /// <response code="404">If the specified sport was not found.</response>
    [AllowAnonymous]
    [HttpGet("{sportId:guid}/configurations")]
    [ProducesResponseType(typeof(IEnumerable<SportConfigurationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfigurationsBySportId([FromRoute] Guid sportId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving configurations for sport {SportId}.", sportId);
        var result = await _mediator.Send(new GetSportConfigurationsBySportIdQuery(sportId), cancellationToken);

        return Ok(result);
    }
}