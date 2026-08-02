using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the execution of <see cref="GetTeamMatchLineupQuery"/> to retrieve a team-specific match lineup protocol.
/// Performs domain boundary validations and maps database projection models to structured <see cref="MatchLineupResponse"/> DTOs.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="GetTeamMatchLineupHandler"/> class.
/// </remarks>
/// <param name="matchLineupRepository">Repository for querying match lineup projections from storage.</param>
/// <param name="matchRepository">Repository for fetching parent match domain entity metadata.</param>
/// <param name="logger">Logger instance for diagnostic logging and audit tracking.</param>
public class GetTeamMatchLineupHandler(
    IMatchLineupRepository matchLineupRepository,
    IMatchRepository matchRepository,
    ILogger<GetTeamMatchLineupHandler> logger) : IRequestHandler<GetTeamMatchLineupQuery, IEnumerable<MatchLineupResponse>>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ILogger<GetTeamMatchLineupHandler> _logger = logger;

    /// <summary>
    /// Processes the <see cref="GetTeamMatchLineupQuery"/> to fetch, validate, and map match lineup entries for a targeted team.
    /// </summary>
    /// <remarks>
    /// The execution pipeline follows these domain verification steps:
    /// <list type="number">
    /// <item><description>Verifies that the target match exists in persistent storage.</description></item>
    /// <item><description>Validates that the requested team is a registered participant (Home or Guest) in the specified match.</description></item>
    /// <item><description>Queries strongly-typed <c>MatchLineupProjection</c> database records via storage functions.</description></item>
    /// <item><description>Transforms projections into immutable <see cref="MatchLineupResponse"/> DTOs.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="request">The query request payload containing the unique match and team identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for asynchronous operation cancellation requests.</param>
    /// <returns>A collection of <see cref="MatchLineupResponse"/> DTOs representing the team's official match lineup protocol.</returns>
    /// <exception cref="NotFoundException">
    /// Thrown when the target match does not exist in storage or when the specified team is not a participant in the match.
    /// </exception>
    public async Task<IEnumerable<MatchLineupResponse>> Handle(GetTeamMatchLineupQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving protocol for Team {TeamId} in Match {MatchId}.", request.TeamId, request.MatchId);

        // 1. Validate Match existence
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null)
        {
            _logger.LogWarning("GetTeamMatchLineup failed: Match {MatchId} not found.", request.MatchId);
            throw new NotFoundException($"Match with ID {request.MatchId} was not found.");
        }

        // 2. Validate Team participation in the match
        if (match.HomeTeamId != request.TeamId && match.GuestTeamId != request.TeamId)
        {
            _logger.LogWarning("GetTeamMatchLineup failed: Team {TeamId} is not a participant in Match {MatchId}.",
                request.TeamId, request.MatchId);
            throw new NotFoundException($"Team with ID {request.TeamId} is not a participant in Match {request.MatchId}.");
        }

        // 3. Fetch strongly-typed projections from repository
        var projections = await _matchLineupRepository.GetTeamMatchLineupAsync(request.MatchId, request.TeamId, cancellationToken);

        // 4. Map projections to response DTOs
        var response = projections.Select(p => new MatchLineupResponse(
            Id: p.Id,
            MatchId: p.MatchId,
            TeamId: p.TeamId,
            PlayerRosterId: p.PlayerRosterId,
            FirstName: p.FirstName,
            LastName: p.LastName,
            Number: p.Number,
            PositionId: p.PositionId,
            PositionName: p.PositionName
        )).ToList();

        _logger.LogInformation("Successfully retrieved {Count} lineup entries for Team {TeamId} in Match {MatchId}.",
            response.Count, request.TeamId, request.MatchId);

        return response;
    }
}