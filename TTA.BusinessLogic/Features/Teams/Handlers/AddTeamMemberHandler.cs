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
        string safeEmail = MaskEmail(command.UserEmail);
        _logger.LogInformation("Processing AddTeamMemberCommand for Email: {Email}, Team: {TeamId}",
            safeEmail, command.TeamId);

        // 1. Resolve User by Email
        var users = await _userRepository.GetByEmailAsync(command.UserEmail, cancellationToken);
        var userList = users.ToList();

        // FIX: Ensure exactly one user is found. 
        // Throwing NotFoundException if 0 or Conflict/InvalidOperation if more than 1.
        if (userList.Count == 0)
        {
            _logger.LogWarning("AddMember failed: User with email {Email} not found.", safeEmail);
            throw new NotFoundException($"User with email {command.UserEmail} was not found.");
        }

        if (userList.Count > 1)
        {
            _logger.LogError("AddMember failed: Multiple users found with the same email {Email}.", safeEmail);
            // Throwing a specialized exception or a generic InvalidOperation to prevent arbitrary assignment
            throw new InvalidOperationException($"Multiple users found with email {command.UserEmail}. Data integrity issue.");
        }

        var user = userList.Single();

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
        membership.UserId = user.Id; // Using the guaranteed unique ID

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

    /// <summary>
    /// Masks an email address to protect PII in logs.
    /// Example: test-user@example.com -> te***@example.com
    /// </summary>
    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            return "****";

        var parts = email.Split('@');
        var name = parts[0];
        var domain = parts[1];

        if (name.Length <= 2)
            return $"***@{domain}";

        return $"{name[..2]}***@{domain}";
    }
}