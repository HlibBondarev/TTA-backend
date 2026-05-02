using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Queries;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing team-related operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TeamsController"/> class.
/// </remarks>
/// <param name="mediator">The mediator instance for dispatching commands.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TeamsController(
    IMediator mediator,
    ILogger<TeamsController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<TeamsController> _logger = logger;

    /// <summary>
    /// Adds a new member to a specific team.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="request">The membership details.</param>
    /// <param name="validator">The request validator.</param>
    /// <returns>The unique identifier of the created membership.</returns>
    /// <remarks>
    /// Access is restricted to users with administrative rights ("TeamAdmin" policy) over the specified club.
    /// </remarks>
    /// <response code="200">Returns the unique identifier of the created team.</response>
    /// <response code="400">If the request data is invalid.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have permission to manage this club.</response>
    [HttpPost("{teamId:guid}/members")]
    [Authorize(Policy = "TeamAdmin")]
    [ProducesResponseType(typeof(TeamMembership), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddMember(
        [FromRoute] Guid teamId,
        [FromBody] AddTeamMemberRequest request,
        [FromServices] IValidator<AddTeamMemberRequest> validator)
    {
        _logger.LogInformation("Executing AddMember action for Team {TeamId}.", teamId);

        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for AddTeamMemberRequest: {Errors}.", validationResult.Errors);
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var command = new AddTeamMemberCommand(
            teamId,
            request.UserEmail,
            request.RoleInTeam,
            request.IsPrimary);

        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Terminates a user's membership and access rights within a team.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="request">The termination details including user email and role.</param>
    /// <param name="validator">The request validator.</param>
    /// <returns>A status indicating the result of the operation.</returns>
    /// <response code="204">If the membership was successfully terminated.</response>
    /// <response code="400">Validation failed or invalid request data.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have permission to manage this team.</response>
    /// <response code="404">If the active membership was not found for the given email and role.</response>
    [HttpDelete("{teamId:guid}/members/terminate")]
    [Authorize(Policy = "TeamAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TerminateMember(
        [FromRoute] Guid teamId,
        [FromBody] TerminateMembershipRequest request,
        [FromServices] IValidator<TerminateMembershipRequest> validator)
    {
        _logger.LogInformation("Executing TerminateMember action for Team {TeamId}, User {Email}.",
            teamId, request.UserEmail.MaskEmail());

        var validationResult = await validator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for TerminateMembershipRequest: {Errors}.", validationResult.Errors);

            // Map FluentValidation errors to ModelState to produce a standard ValidationProblemDetails response
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        // Mapping route data and request DTO to the command
        var command = new TerminateMembershipCommand(
            teamId,
            request.UserEmail,
            request.RoleInTeam,
            request.LeftAt);

        await _mediator.Send(command);

        return NoContent();
    }

    /// <summary>
    /// Retrieves a list of all active members in a specific team, including their profile details.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <returns>A collection of team members with their profile information.</returns>
    /// <remarks>
    /// This endpoint is publicly accessible. It returns a flattened JSON structure 
    /// containing both membership data and user identity details (Name, Email).
    /// </remarks>
    /// <response code="200">Returns the list of team members. Returns an empty list if no members are found.</response>
    /// <response code="404">If the specified team does not exist in the system.</response>
    [AllowAnonymous]
    [HttpGet("{teamId:guid}/members")]
    [ProducesResponseType(typeof(List<TeamMemberResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMembers([FromRoute] Guid teamId)
    {
        _logger.LogInformation("Executing GetMembers action for Team {TeamId}.", teamId);

        var query = new GetTeamMembersQuery(teamId);

        // The handler will return an empty list if no members exist, 
        // and throws a NotFoundException if the team itself is missing.
        var result = await _mediator.Send(query);

        return Ok(result);
    }
}