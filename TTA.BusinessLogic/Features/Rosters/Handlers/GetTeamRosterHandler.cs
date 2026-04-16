using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Rosters.DTOs;
using TTA.BusinessLogic.Features.Rosters.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Rosters.Handlers;

/// <summary>
/// Handles the retrieval of a specific team's roster for a tournament.
/// Maps dynamic database results to structured response DTOs.
/// </summary>
public class GetTeamRosterHandler(
    IRosterRepository rosterRepository,
    ITeamRepository teamRepository,
    ILogger<GetTeamRosterHandler> logger) : IRequestHandler<GetTeamRosterQuery, IEnumerable<RosterPlayerResponse>>
{
    private readonly IRosterRepository _rosterRepository = rosterRepository;
    private readonly ITeamRepository _teamRepository = teamRepository;
    private readonly ILogger<GetTeamRosterHandler> _logger = logger;

    /// <summary>
    /// Processes the query to fetch and map the roster data.
    /// </summary>
    /// <param name="request">The query containing tournament and team identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of <see cref="RosterPlayerResponse"/> objects.</returns>
    /// <exception cref="NotFoundException">Thrown if the team does not exist.</exception>
    public async Task<IEnumerable<RosterPlayerResponse>> Handle(GetTeamRosterQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving roster for Team {TeamId} in Tournament {TournamentId}.",
            request.TeamId, request.TournamentId);

        // 1. Check if team exists (optional, but good for precise 404 vs empty list)
        var team = await _teamRepository.GetByIdAsync(request.TeamId, cancellationToken);
        if (team == null)
        {
            _logger.LogWarning("GetTeamRoster failed: Team {TeamId} not found.", request.TeamId);
            throw new NotFoundException($"Team with ID {request.TeamId} was not found.");
        }

        // 2. Fetch data from repository
        // We use dynamic results from Dapper to avoid creating an intermediate persistence model
        var rawRoster = await _rosterRepository.GetTeamRosterAsync(request.TournamentId, request.TeamId, cancellationToken);

        // 3. Map dynamic rows to structured DTOs
        var response = rawRoster.Select(row => new RosterPlayerResponse(
            Id: row.id,
            PlayerId: row.playerid,
            FirstName: row.firstname,
            LastName: row.lastname,
            PositionId: row.positionid,
            PositionName: row.positionname,
            Number: row.number
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} players for Team {TeamId}.",
            response.Count, request.TeamId);

        return response;
    }
}