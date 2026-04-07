using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.Common.Enums;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unique identifier of the created membership.</returns>
    /// <exception cref="NotFoundException">Thrown when the team or user does not exist.</exception>
    public async Task<Guid> Handle(AddTeamMemberCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing AddTeamMemberCommand for the user email in Team: {TeamId}",
            command.TeamId);

        // 1. Resolve User by Email
        var user = await _userRepository.GetByEmailAsync(command.UserEmail, cancellationToken);
        if (user == null || !user.Any())
        {
            _logger.LogWarning("AddMember failed: User with email {Email} not found.", command.UserEmail);
            throw new NotFoundException($"User with email {command.UserEmail} was not found.");
        }

        // 2. Validate Team existence
        var team = await _teamRepository.GetByIdAsync(command.TeamId, cancellationToken);
        if (team == null)
        {
            _logger.LogWarning("AddMember failed: Team {TeamId} not found.", command.TeamId);
            throw new NotFoundException($"Team with ID {command.TeamId} was not found.");
        }

        // 3. Map TeamRole to system AppRole enum
        AppRole appRole = MapToAppRole(command.RoleInTeam);

        // 4. Create model and persist via Repository
        var membership = command.ToModel();
        membership.UserId = user.First().Id; // Assuming email is unique and taking the first match

        // Passing the enum directly to the repository
        var result = await _membershipRepository.CreateMembershipWithPolicyAsync(membership, appRole, cancellationToken);

        _logger.LogInformation("Successfully persisted membership {MembershipId} with AppRole {AppRole}",
            result.Id, appRole.ToString());

        return result.Id;
    }

    /// <summary>
    /// Maps internal team roles to system-wide application roles.
    /// </summary>
    private static AppRole MapToAppRole(TeamRole role) => role switch
    {
        TeamRole.HeadCoach or TeamRole.AssistantCoach or TeamRole.ClubDirector => AppRole.FullControl,
        TeamRole.TeamManager or TeamRole.Analyst => AppRole.Editor,
        _ => AppRole.Viewer
    };
}