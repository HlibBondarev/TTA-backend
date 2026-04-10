using MediatR;
using Microsoft.Extensions.Logging;
using System.Transactions;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.Common.Extensions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handles the termination of a team membership and its associated access policies.
/// This handler performs a soft delete by setting expiration dates in a single transaction.
/// </summary>
/// <remarks>
/// Logic:
/// 1. Finds the active membership by email and role.
/// 2. Finds the corresponding access policy for the user in the team scope.
/// 3. Updates both entities with the termination date (LeftAt) in a transaction.
/// </remarks>
public class TerminateMembershipHandler(
    ITeamMembershipRepository membershipRepository,
    IAccessRepository accessRepository,
    ILogger<TerminateMembershipHandler> logger) : IRequestHandler<TerminateMembershipCommand, bool>
{
    private readonly ITeamMembershipRepository _membershipRepository = membershipRepository;
    private readonly IAccessRepository _accessRepository = accessRepository;
    private readonly ILogger<TerminateMembershipHandler> _logger = logger;

    /// <summary>
    /// Processes the termination command.
    /// </summary>
    /// <param name="command">The command containing team ID, user email, role, and optional termination date.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. 
    /// The task result is <c>true</c> if termination was successful; otherwise, <c>false</c>.
    /// </returns>
    public async Task<bool> Handle(TerminateMembershipCommand command, CancellationToken cancellationToken)
    {
        string safeEmail = command.UserEmail.MaskEmail();

        _logger.LogInformation(
            "Starting termination process for User: {Email}, Role: {Role}, Team: {TeamId}",
            safeEmail, command.RoleInTeam, command.TeamId);

        // 1. Resolve membership
        var membership = await _membershipRepository.GetActiveMembershipByEmailAndRoleAsync(
            command.TeamId,
            command.UserEmail,
            command.RoleInTeam,
            cancellationToken);

        if (membership == null)
        {
            _logger.LogWarning("Termination failed: Active membership for {Email} not found in team {TeamId}.",
                safeEmail, command.TeamId);
            return false;
        }

        // 2. Resolve access policy
        var accessPolicy = await _accessRepository.GetActiveTeamPolicyAsync(
            membership.UserId,
            command.TeamId,
            cancellationToken);

        // Determine termination time (Command value or current UTC)
        var terminationDate = command.LeftAt ?? DateTime.UtcNow;

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        // 3. Update Membership
        membership.LeftAt = terminationDate;
        await _membershipRepository.TerminateMembershipAsync(membership, cancellationToken);

        // 4. Update Access Policy
        if (accessPolicy != null)
        {
            accessPolicy.ExpiresAt = terminationDate;
            await _accessRepository.RemoveAccessAsync(accessPolicy, cancellationToken);
        }

        scope.Complete();

        _logger.LogInformation("Successfully terminated membership and access for User {UserId}. Effective date: {Date}",
            membership.UserId, terminationDate);

        return true;
    }
}