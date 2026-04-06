using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Queries;
using TTA.Common.Exceptions;
using TTA.Common.Extensions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handles the retrieval of team members, ensuring the team exists before fetching data.
/// </summary>
public class GetTeamMembersHandler(
    ITeamMembershipRepository membershipRepository,
    ITeamRepository teamRepository,
    ILogger<GetTeamMembersHandler> logger) : IRequestHandler<GetTeamMembersQuery, IEnumerable<TeamMemberResponse>>
{
    private readonly ITeamMembershipRepository _membershipRepository = membershipRepository;
    private readonly ITeamRepository _teamRepository = teamRepository;
    private readonly ILogger<GetTeamMembersHandler> _logger = logger;

    /// <summary>
    /// Validates team existence and returns a list of members mapped from a JSON string.
    /// </summary>
    /// <param name="request">The query containing TeamId.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of <see cref="TeamMemberResponse"/>.</returns>
    /// <exception cref="NotFoundException">Thrown when the team does not exist.</exception>
    public async Task<IEnumerable<TeamMemberResponse>> Handle(GetTeamMembersQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Validating existence of Team {TeamId} before member retrieval.", request.TeamId);

        // 1. Validate resource existence
        // Using TeamRepository.GetByIdAsync inherited from EntityRepositoryBase
        var team = await _teamRepository.GetByIdAsync(request.TeamId, cancellationToken);
        if (team == null)
        {
            _logger.LogWarning("Member retrieval failed: Team {TeamId} not found.", request.TeamId);
            throw new NotFoundException($"Team with ID {request.TeamId} was not found.");
        }

        // 2. Fetch raw JSON from repository using the SQL function
        var json = await _membershipRepository.GetMembersJsonAsync(request.TeamId, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogDebug("No active members found for Team {TeamId}. Returning empty list.", request.TeamId);
            return [];
        }

        // 3. Deserialize using the application's default JSON settings
        var options = new JsonSerializerOptions().GetDefault();
        var result = JsonSerializer.Deserialize<IEnumerable<TeamMemberResponse>>(json, options);

        return result ?? [];
    }
}