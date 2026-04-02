using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TTA.BusinessLogic.Features.Clubs.Commands;
using TTA.BusinessLogic.Features.Clubs.DTOs;
using TTA.BusinessLogic.Features.Players.Commands;
using TTA.BusinessLogic.Features.Players.DTOs;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing club-related operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ClubsController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching commands.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ClubsController(
    IMediator mediator,
    ILogger<ClubsController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<ClubsController> _logger = logger;

    /// <summary>
    /// Creates a new club and assigns the current user as the owner.
    /// </summary>
    /// <param name="request">The club creation request data.</param>
    /// <param name="validator">The validator for the creation request (injected via method).</param>
    /// <returns>The unique identifier of the newly created club.</returns>
    /// <response code="200">Returns the unique identifier of the created club.</response>
    /// <response code="400">If the request data is invalid.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="409">If a club with the same name already exists.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateClubRequest request,
        [FromServices] IValidator<CreateClubRequest> validator)
    {
        _logger.LogInformation("Starting Create action in {ControllerName} for Club: {ClubName}.",
            nameof(ClubsController), request.Name);

        // Explicitly validate the incoming request
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreateClubRequest: {Errors}.",
                validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // Extract the Auth0 unique identifier (sub claim)
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("Failed to extract User ID from claims for an authorized request.");
            return Unauthorized("User identifier missing from token.");
        }

        // Map DTO to Command and dispatch via MediatR
        var command = new CreateClubCommand(request.Name, request.CityId, userId);

        _logger.LogInformation("Dispatching CreateClubCommand for User: {UserId}.", userId);
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Registers a new player within a specific club context.
    /// </summary>
    /// <param name="clubId">The unique identifier of the club where the player will be registered.</param>
    /// <param name="request">The player creation request data.</param>
    /// <param name="validator">The validator for the player creation request.</param>
    /// <returns>The unique identifier of the newly created player.</returns>
    /// <remarks>
    /// This endpoint is protected by the "ClubAdmin" policy. 
    /// The user must have 'FullControl' permissions for the club specified in the route.
    /// </remarks>
    /// <response code="200">Returns the unique identifier of the created player.</response>
    /// <response code="400">If the request data is invalid or validation fails.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have administrative rights over this club.</response>
    [HttpPost("{clubId:guid}/players")]
    [Authorize(Policy = "ClubAdmin")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreatePlayer(
        [FromRoute] Guid clubId,
        [FromBody] CreatePlayerRequest request,
        [FromServices] IValidator<CreatePlayerRequest> validator)
    {
        _logger.LogInformation("Executing CreatePlayer action for a new player in Club {ClubId}.", clubId);

        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for CreatePlayerRequest in Club {ClubId}: {Errors}.",
                clubId, validationResult.Errors);
            return BadRequest(validationResult.Errors);
        }

        // Use clubId from the route to ensure the player is added to the authorized club
        var command = new CreatePlayerCommand(
            clubId,
            request.FirstName,
            request.LastName,
            request.BirthDate,
            request.Gender);

        _logger.LogInformation("Dispatching CreatePlayerCommand for Club: {ClubId}.", clubId);
        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Extracts the user's unique identifier from the current security context.
    /// </summary>
    /// <returns>The Auth0 'sub' claim or NameIdentifier; otherwise, an empty string.</returns>
    private string GetUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value
               ?? string.Empty;
    }
}