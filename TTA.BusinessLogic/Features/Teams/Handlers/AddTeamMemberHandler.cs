using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.Common.Enums;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handles the addition of a member to a team, ensuring both the team and the user exist,
/// preventing duplicate active roles, and managing primary team status.
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
    /// Validates dependencies and persists the new team membership with integrity checks.
    /// </summary>
    /// <param name="command">The command containing membership details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unique identifier of the created membership.</returns>
    /// <exception cref="NotFoundException">Thrown when the team or user does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when data integrity issues are detected (e.g., duplicate emails).</exception>
    /// <exception cref="ConflictException">Thrown when the user already has an active membership with the same role.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the TeamRole cannot be mapped to an AppRole.</exception>
    public async Task<Guid> Handle(AddTeamMemberCommand command, CancellationToken cancellationToken)
    {
        string safeEmail = MaskEmail(command.UserEmail);
        _logger.LogInformation("Processing AddTeamMemberCommand for Email: {Email}, Team: {TeamId}",
            safeEmail, command.TeamId);

        // 1. Resolve User by Email
        var users = await _userRepository.GetByEmailAsync(command.UserEmail, cancellationToken);
        var userList = users.ToList();

        if (userList.Count == 0)
        {
            _logger.LogWarning("AddMember failed: User with email {Email} not found.", safeEmail);
            throw new NotFoundException($"User with email {command.UserEmail} was not found.");
        }

        if (userList.Count > 1)
        {
            _logger.LogError("AddMember failed: Multiple users found with the same email {Email}.", safeEmail);
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

        // 4. Prepare Entity
        var membership = command.ToModel(user.Id);

        try
        {
            // 5. Persistence with DB-level validation (Active Role Check & Primary Flag Reset)
            var result = await _membershipRepository.CreateMembershipWithPolicyAsync(membership, appRole, cancellationToken);

            _logger.LogInformation("Successfully persisted membership {MembershipId} for User {UserId} with AppRole {AppRole}",
                result.Id, user.Id, appRole.ToString());

            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            _logger.LogWarning("Member Refinement Failed: Duplicate active role {Role} for User {UserId}",
                command.RoleInTeam, user.Id);

            throw new ConflictException($"The user is already an active '{command.RoleInTeam}' in this team.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while creating team membership for User {UserId} in Team {TeamId}",
                user.Id, command.TeamId);
            throw;
        }
    }

    /// <summary>
    /// Maps internal team roles to system-wide application roles.
    /// Throws an exception if the role is not explicitly mapped to prevent hidden bugs.
    /// </summary>
    /// <param name="role">The team role to map.</param>
    /// <returns>The corresponding <see cref="AppRole"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an unmapped TeamRole is provided.</exception>
    private static AppRole MapToAppRole(TeamRole role) => role switch
    {
        TeamRole.HeadCoach or TeamRole.AssistantCoach or TeamRole.ClubDirector => AppRole.FullControl,
        TeamRole.TeamManager or TeamRole.Analyst => AppRole.Editor,
        TeamRole.Player => AppRole.Viewer,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, $"No mapping defined for {role}")
    };

    /// <summary>
    /// Masks an email address to protect PII in logs.
    /// Example: test-user@example.com -> te***@example.com
    /// </summary>
    /// <param name="email">The plaintext email to mask.</param>
    /// <returns>A masked version of the email string.</returns>
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