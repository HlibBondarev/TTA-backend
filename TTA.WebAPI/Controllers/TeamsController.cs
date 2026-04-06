using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Queries;
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
    /// <returns>The created team membership record.</returns>
    /// <remarks>
    /// Access is restricted to users with administrative rights ("ClubAdmin" policy) over the specified club.
    /// </remarks>
    /// <response code="200">Returns the unique identifier of the created team.</response>
    /// <response code="400">If the request data is invalid.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have permission to manage this club.</response>
    [HttpPost("{teamId:guid}/members")]
    [Authorize(Policy = "TeamAdmin")]
    [ProducesResponseType(typeof(TeamMembership), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
            return BadRequest(validationResult.Errors);
        }

        var command = new AddTeamMemberCommand(
            teamId,
            request.UserId,
            request.RoleInTeam,
            request.IsPrimary);

        var result = await _mediator.Send(command);

        return Ok(result);
    }

    /// <summary>
    /// Terminates a user's membership in a team.
    /// </summary>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="membershipId">The unique identifier of the membership record to terminate.</param>
    /// <returns>True if the operation was successful.</returns>
    /// <remarks>
    /// This is a soft-delete operation that sets the 'LeftAt' timestamp.
    /// Access is restricted to users with administrative rights ("ClubAdmin" policy).
    /// </remarks>
    /// <response code="200">If the membership was successfully terminated.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user does not have permission to manage this club.</response>
    /// <response code="404">If the membership record was not found.</response>
    [HttpDelete("{teamId:guid}/members/{membershipId:guid}")]
    [Authorize(Policy = "TeamAdmin")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TerminateMember([FromRoute] Guid teamId, [FromRoute] Guid membershipId)
    {
        _logger.LogInformation("Executing TerminateMember action for Membership {MembershipId} in Team {TeamId}.",
            membershipId, teamId);

        var command = new TerminateMembershipCommand(membershipId);
        var result = await _mediator.Send(command);

        if (!result)
        {
            return NotFound();
        }

        return Ok(result);
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