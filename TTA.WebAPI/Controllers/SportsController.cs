using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.EventDefinitions.Queries;
using TTA.BusinessLogic.Features.SportConfigurations.DTOs;
using TTA.BusinessLogic.Features.SportConfigurations.Queries;
using TTA.BusinessLogic.Features.Sports.DTOs;
using TTA.BusinessLogic.Features.Sports.Queries;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Extensions;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing and retrieving sport entities, configurations, and TTA event definitions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SportsController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching queries and commands.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
/// <param name="auth0Settings">The settings for Auth0 authentication.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SportsController(
    IMediator mediator,
    ILogger<SportsController> logger,
    Auth0Settings auth0Settings) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<SportsController> _logger = logger;
    private readonly Auth0Settings _auth0Settings = auth0Settings;

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

    /// <summary>
    /// Retrieves all available active event definitions (system defaults and user custom ones) for a sport context.
    /// </summary>
    /// <param name="sportId">The unique identifier of the target sport.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of available event definitions with preset state.</returns>
    /// <response code="200">Returns the collection of available event definitions.</response>
    /// <response code="401">If the user is not authenticated.</response>
    [HttpGet("{sportId:guid}/event-definitions")]
    [ProducesResponseType(typeof(IEnumerable<EventDefinitionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAvailableEventDefinitions([FromRoute] Guid sportId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetAvailableEventDefinitions for sport {SportId}.", sportId);
        var userId = this.GetUserId(_auth0Settings);

        var query = new GetAvailableEventDefinitionsQuery(sportId, userId);
        var result = await _mediator.Send(query, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Creates a new custom user-owned event definition for a specific sport.
    /// </summary>
    /// <param name="sportId">The unique identifier of the target sport.</param>
    /// <param name="request">The request body containing custom event definition parameters.</param>
    /// <param name="validator">The validator instance for the request DTO.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The newly created custom event definition response.</returns>
    /// <response code="201">Returns the created custom event definition.</response>
    /// <response code="400">If the request payload is invalid.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="409">If a business rule is violated (e.g. attempting to modify system default or soft-deleted definitions).</response>
    [HttpPost("{sportId:guid}/event-definitions/custom")]
    [ProducesResponseType(typeof(EventDefinitionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCustomEventDefinition(
        [FromRoute] Guid sportId,
        [FromBody] CreateCustomEventDefinitionRequest request,
        [FromServices] IValidator<CreateCustomEventDefinitionRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CreateCustomEventDefinition for sport {SportId}.", sportId);

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreateCustomEventDefinitionRequest: {Errors}.", validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        var userId = this.GetUserId(_auth0Settings);
        var command = request.ToCommand(sportId, userId);

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Persists the user's active event definition preset layout and ordering for a specific sport.
    /// </summary>
    /// <param name="sportId">The unique identifier of the target sport.</param>
    /// <param name="request">The request payload containing ordered event definition IDs.</param>
    /// <param name="validator">The validator instance for the request DTO.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>HTTP 200 OK if successful.</returns>
    /// <response code="200">If the preset was successfully saved.</response>
    /// <response code="400">If the request payload is invalid.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="409">If an event definition ID is invalid, soft-deleted, or unauthorized.</response>
    [HttpPut("{sportId:guid}/event-definitions/preset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveUserEventPreset(
        [FromRoute] Guid sportId,
        [FromBody] SaveUserEventPresetRequest request,
        [FromServices] IValidator<SaveUserEventPresetRequest> validator,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing SaveUserEventPreset for sport {SportId}.", sportId);

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for SaveUserEventPresetRequest: {Errors}.", validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        var userId = this.GetUserId(_auth0Settings);
        var command = request.ToCommand(userId, sportId);

        await _mediator.Send(command, cancellationToken);

        return Ok();
    }
}