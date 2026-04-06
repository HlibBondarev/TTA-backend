using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handles the termination of a team membership.
/// </summary>
public class TerminateMembershipHandler(
    ITeamMembershipRepository repository,
    ILogger<TerminateMembershipHandler> logger) : IRequestHandler<TerminateMembershipCommand, bool>
{
    private readonly ITeamMembershipRepository _repository = repository;
    private readonly ILogger<TerminateMembershipHandler> _logger = logger;

    /// <summary>
    /// Executes the termination logic via the repository.
    /// </summary>
    /// <param name="command">The command containing the membership ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the membership was successfully terminated; otherwise, false.</returns>
    public async Task<bool> Handle(TerminateMembershipCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to terminate membership {MembershipId}", command.MembershipId);

        var deleted = await _repository.TerminateMembershipAsync(command.MembershipId, cancellationToken);

        if (deleted)
        {
            _logger.LogInformation("Membership {MembershipId} successfully terminated", command.MembershipId);
        }
        else
        {
            _logger.LogWarning("Membership {MembershipId} was not found or already terminated", command.MembershipId);
        }

        return deleted;
    }
}