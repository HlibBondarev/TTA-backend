using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Players.Commands;
using TTA.BusinessLogic.Features.Players.DTOs;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing player (athlete) operations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PlayersController(
    IMediator mediator,
    ILogger<PlayersController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Registers a new player in the system.
    /// </summary>
    /// <param name="request">The player creation request data.</param>
    /// <param name="validator">The validator for the request (injected via FromServices).</param>
    /// <returns>The unique identifier of the newly created player.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePlayerRequest request,
        [FromServices] IValidator<CreatePlayerRequest> validator)
    {
        _logger.LogInformation("Executing Create action for Player: {FirstName} {LastName}.",
            request.FirstName, request.LastName);

        // 1. Explicit Validation
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreatePlayerRequest: {Errors}.",
                validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // 2. Map DTO to Command 
        // Note: We don't need UserId from claims here as HomeClubId is part of the request, 
        // and security checks will happen in a pipeline or handler later.
        var command = new CreatePlayerCommand(
            request.HomeClubId,
            request.FirstName,
            request.LastName,
            request.BirthDate,
            request.Gender);

        // 3. Dispatch
        var result = await _mediator.Send(command);

        return Ok(result);
    }
}