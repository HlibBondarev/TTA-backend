using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handles the addition of a member to a team, ensuring both the team and the user exist.
/// </summary>
public class AddTeamMemberHandler(
    ITeamMembershipRepository membershipRepository,
    ITeamRepository teamRepository,
    IUserRepository userRepository,
    ILogger<AddTeamMemberHandler> logger) : IRequestHandler<AddTeamMemberCommand, Guid>
{
    private readonly ITeamMembershipRepository _membershipRepository = membershipRepository;
    private readonly ITeamRepository _teamRepository = teamRepository;
    private readonly IUserRepository _userRepository = userRepository;
    private readonly ILogger<AddTeamMemberHandler> _logger = logger;

    /// <summary>
    /// Validates dependencies and persists the new team membership.
    /// </summary>
    /// <param name="command">The command containing membership details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unique identifier of the created membership.</returns>
    /// <exception cref="NotFoundException">Thrown when the team or user does not exist.</exception>
    public async Task<Guid> Handle(AddTeamMemberCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Processing AddTeamMemberCommand for User: {UserId}, Team: {TeamId}",
            command.UserId, command.TeamId);

        // 1. Validate Team existence
        var team = await _teamRepository.GetByIdAsync(command.TeamId, ct);
        if (team == null)
        {
            _logger.LogWarning("AddMember failed: Team {TeamId} not found.", command.TeamId);
            throw new NotFoundException($"Team with ID {command.TeamId} was not found.");
        }

        // 2. Validate User existence
        var user = await _userRepository.GetByIdAsync(command.UserId, ct);
        if (user == null)
        {
            _logger.LogWarning("AddMember failed: User {UserId} not found.", command.UserId);
            throw new NotFoundException($"User with ID {command.UserId} was not found.");
        }

        // 3. Map and Persist
        var membership = command.ToModel();
        var result = await _membershipRepository.CreateMembershipAsync(membership, ct);

        _logger.LogInformation("Successfully persisted membership with ID: {MembershipId}", result.Id);

        return result.Id;
    }
}