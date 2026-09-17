using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Extensions;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing custom Technical and Tactical Action (TTA) event definitions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="EventDefinitionsController"/> class.
/// </remarks >
/// <param name="mediator">The mediator instance for dispatching commands.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
/// <param name="auth0Settings" >The settings for Auth0 authentication.</param>
[Authorize]
[ApiController]
[Route("api/event-definitions")]
public class EventDefinitionsController(
    IMediator mediator,
    ILogger<EventDefinitionsController> logger,
    Auth0Settings auth0Settings) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<EventDefinitionsController> _logger = logger;
    private readonly Auth0Settings _auth0Settings = auth0Settings;

    /// <summary>
    /// Soft-deletes a custom user-owned event definition and removes it from active presets.
    /// </summary>
    /// <param name="id">The unique identifier of the custom event definition to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>No content on successful deletion.</returns>
    /// <response code="204" >If the custom event definition was successfully soft-deleted.</response >
    /// <response code="401" >If the request is not authenticated.</response>
    /// <response code="404" >If the custom event definition was not found or not owned by the user.</response>
    [HttpDelete("custom/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCustomEventDefinition(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeleteCustomEventDefinition action for ID {Id}.", id);

        var userId = this.GetUserId(_auth0Settings);
        var command = new DeleteCustomEventDefinitionCommand(id, userId);

        var result = await _mediator.Send(command, cancellationToken);

        if (!result)
        {
            _logger.LogWarning("Soft-delete custom event definition failed for ID {Id} and User {UserId}.", id, userId);
            return NotFound("The specified custom event definition was not found or is not owned by the current user.");
        }

        return NoContent();
    }
}