using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handler for processing <see cref="GetTeamByIdQuery"/> requests.
/// </summary>
/// <param name="teamRepository">The repository instance for team database operations.</param>
/// <param name="logger">The logger instance for diagnostic information.</param>
public class GetTeamByIdHandler(
    ITeamRepository teamRepository,
    ILogger<GetTeamByIdHandler> logger)
    : IRequestHandler<GetTeamByIdQuery, TeamResponse>
{
    private readonly ITeamRepository _teamRepository = teamRepository;
    private readonly ILogger<GetTeamByIdHandler> _logger = logger;

    /// <summary>
    /// Handles the execution of the query to retrieve a team by its ID.
    /// </summary>
    /// <param name="request">The query request payload containing the team ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="TeamResponse"/> record.</returns>
    /// <exception cref="NotFoundException">Thrown when no team exists with the specified ID.</exception>
    public async Task<TeamResponse> Handle(GetTeamByIdQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving details for team {TeamId}.", request.TeamId);

        var team = await _teamRepository.GetByIdAsync(request.TeamId, cancellationToken);

        if (team is null)
        {
            _logger.LogWarning("Team retrieval failed: Team with ID {TeamId} not found.", request.TeamId);
            throw new NotFoundException($"Team with ID '{request.TeamId}' was not found.");
        }

        return new TeamResponse(
            team.Id,
            team.ClubId,
            team.SportId,
            team.Name,
            team.MinBirthYear,
            (int)team.Gender,
            team.CreatedAt);
    }
}