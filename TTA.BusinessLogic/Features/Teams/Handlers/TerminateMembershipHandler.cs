using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.Common.Exceptions;
using TTA.Common.Extensions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handles the termination of a team membership and its associated access policies.
/// This handler performs a soft delete by setting expiration dates in a single database transaction.
/// </summary>
public class TerminateMembershipHandler(
    ITeamMembershipRepository membershipRepository,
    IAccessRepository accessRepository,
    ILogger<TerminateMembershipHandler> logger) : IRequestHandler<TerminateMembershipCommand, bool>
{
    private readonly ITeamMembershipRepository _membershipRepository = membershipRepository;
    private readonly IAccessRepository _accessRepository = accessRepository;
    private readonly ILogger<TerminateMembershipHandler> _logger = logger;

    /// <summary>
    /// Processes the membership termination using an implicit rollback pattern.
    /// </summary>
    /// <param name="command">Termination details including Team, Email, and Role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A boolean indicating success.</returns>
    /// <exception cref="NotFoundException">Thrown when no active membership is found.</exception>
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
            _logger.LogWarning("Termination failed: Active membership for {Email} not found.", safeEmail);
            throw new NotFoundException($"Active membership for {command.UserEmail} not found in team {command.TeamId}.");
        }

        // 2. Resolve access policy
        var accessPolicy = await _accessRepository.GetActiveTeamPolicyAsync(
            membership.UserId,
            command.TeamId,
            cancellationToken);

        var terminationDate = command.LeftAt ?? DateTime.UtcNow;

        // 3. Open connection and start transaction. 
        // If transaction.Commit() is not called due to an exception, 
        // Dispose() will automatically trigger a rollback.
        using var connection = await _membershipRepository.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // 4. Update Membership status
        membership.LeftAt = terminationDate;
        await _membershipRepository.TerminateMembershipAsync(membership, connection, transaction, cancellationToken);

        // 5. Update Access Policy if it exists
        if (accessPolicy != null)
        {
            accessPolicy.ExpiresAt = terminationDate;
            await _accessRepository.RemoveAccessAsync(accessPolicy, connection, transaction, cancellationToken);
        }

        // 6. Commit the transaction
        transaction.Commit();

        _logger.LogInformation("Successfully terminated membership for User {UserId}.", membership.UserId);

        return true;
    }
}