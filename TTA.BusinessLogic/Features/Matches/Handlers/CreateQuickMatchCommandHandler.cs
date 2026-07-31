using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.Common.Enums;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Features.Matches.Handlers;
/// <summary>
/// Handles the execution of <see cref="CreateQuickMatchCommand"/> to provision JIT entities, assign access policies, 
/// create the match, and initialize starting lineups.
/// </summary>
/// <param name="matchRepository">The match repository for database operations and JIT provisioning.</param>
/// <param name="accessRepository">The access repository for checking and granting team access policies.</param>
/// <param name="sportRepository">The sport repository for retrieving sport metadata and default configurations.</param>
/// <param name="sportConfigurationRepository">The sport configuration repository for retrieving sport configuration rules.</param>
/// <param name="rosterRepository">The roster repository for fetching tournament rosters.</param>
/// <param name="matchLineupRepository">The match lineup repository for copying players into match lineups.</param>
/// <param name="logger">The logger instance for diagnostic messages.</param>
public class CreateQuickMatchCommandHandler(
    IMatchRepository matchRepository,
    IAccessRepository accessRepository,
    ISportRepository sportRepository,
    ISportConfigurationRepository sportConfigurationRepository,
    IRosterRepository rosterRepository,
    IMatchLineupRepository matchLineupRepository,
    ILogger<CreateQuickMatchCommandHandler> logger) : IRequestHandler<CreateQuickMatchCommand, QuickMatchResponse>
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IAccessRepository _accessRepository = accessRepository;
    private readonly ISportRepository _sportRepository = sportRepository;
    private readonly ISportConfigurationRepository _sportConfigurationRepository = sportConfigurationRepository;
    private readonly IRosterRepository _rosterRepository = rosterRepository;
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<CreateQuickMatchCommandHandler> _logger = logger;

    /// <summary>
    /// Provisions quick match infrastructure, verifies or grants team editor access policies, and copies starter roster entries into the match lineup.
    /// Performs compensating cleanup if post-creation provisioning fails.
    /// </summary>
    /// <param name="command">The command containing quick match setup parameters and authenticated user details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="QuickMatchResponse"/> containing the newly created match details.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if quick match infrastructure creation fails, or if the specified sport/sport configuration is missing.</exception>
    public async Task<QuickMatchResponse> Handle(
        CreateQuickMatchCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Initiating quick match creation for SportId {SportId} by User {UserId}.",
            command.Request.SportId, command.UserId);

        // 1. Atomic JIT provisioning and Match entity creation via PostgreSQL stored function
        var quickMatchProjection = await _matchRepository.CreateQuickMatchAsync(
            command.Request.SportId,
            command.Request.ConfigurationId,
            cancellationToken);

        if (quickMatchProjection == null)
        {
            _logger.LogError("Failed to provision quick match infrastructure for SportId {SportId}.", command.Request.SportId);
            throw new KeyNotFoundException($"Failed to provision quick match infrastructure for SportId: {command.Request.SportId}");
        }

        _logger.LogInformation("Quick match {MatchId} created with HomeTeam {HomeTeamId} and GuestTeam {GuestTeamId}.",
            quickMatchProjection.Id, quickMatchProjection.HomeTeamId, quickMatchProjection.GuestTeamId);

        try
        {
            // 2. Ensure TeamEditor access policy JIT for Home Squad
            var activePolicy = await _accessRepository.GetActiveTeamPolicyAsync(
                command.UserId,
                quickMatchProjection.HomeTeamId,
                cancellationToken);

            if (activePolicy == null)
            {
                _logger.LogDebug("Granting TeamEditor policy for User {UserId} on HomeTeam {HomeTeamId}.",
                    command.UserId, quickMatchProjection.HomeTeamId);

                var newPolicy = new AccessPolicy
                {
                    Id = Guid.NewGuid(),
                    UserId = command.UserId,
                    TargetType = TargetScope.Team,
                    TargetId = quickMatchProjection.HomeTeamId,
                    Role = AppRole.Editor,
                    CreatedAt = DateTime.UtcNow
                };

                await _accessRepository.AddAccessAsync(newPolicy, cancellationToken);
            }

            // 3. Resolve target SportConfiguration ID
            Guid targetConfigurationId;
            if (command.Request.ConfigurationId.HasValue && command.Request.ConfigurationId.Value != Guid.Empty)
            {
                targetConfigurationId = command.Request.ConfigurationId.Value;
            }
            else
            {
                var sport = await _sportRepository.GetByIdAsync(command.Request.SportId, cancellationToken);
                if (sport == null)
                {
                    _logger.LogError("Sport with ID {SportId} was not found.", command.Request.SportId);
                    throw new KeyNotFoundException($"Sport with ID {command.Request.SportId} was not found.");
                }

                targetConfigurationId = sport.DefaultConfigId;
            }

            // 4. Fetch Sport Configuration to obtain LineupLimit
            var sportConfig = await _sportConfigurationRepository.GetByIdAsync(targetConfigurationId, cancellationToken);
            if (sportConfig == null)
            {
                _logger.LogError("Sport configuration with ID {ConfigurationId} was not found.", targetConfigurationId);
                throw new KeyNotFoundException($"Sport configuration with ID {targetConfigurationId} was not found.");
            }

            // 5. Retrieve Home Squad tournament roster
            var homeRoster = await _rosterRepository.GetTeamRosterAsync(
                quickMatchProjection.TournamentId,
                quickMatchProjection.HomeTeamId,
                cancellationToken);

            // 6. Slice top N starters based on LineupLimit with explicit cast to Guid
            var starterRosterIds = homeRoster
                .Take(sportConfig.LineupLimit)
                .Select(r => (Guid)r.id)
                .ToArray();

            // 7. Populate starting lineup for Home Squad
            if (starterRosterIds.Length > 0)
            {
                _logger.LogDebug("Populating starting lineup with {Count} players for Match {MatchId}.",
                    starterRosterIds.Length, quickMatchProjection.Id);

                await _matchLineupRepository.CopyFromRosterAsync(
                    quickMatchProjection.Id,
                    quickMatchProjection.HomeTeamId,
                    starterRosterIds,
                    cancellationToken);
            }

            _logger.LogInformation("Successfully completed quick match creation for Match {MatchId}.", quickMatchProjection.Id);

            // 8. Map and return QuickMatchResponse DTO
            return new QuickMatchResponse
            {
                Id = quickMatchProjection.Id,
                TournamentId = quickMatchProjection.TournamentId,
                HomeTeamId = quickMatchProjection.HomeTeamId,
                GuestTeamId = quickMatchProjection.GuestTeamId,
                ScheduledAt = quickMatchProjection.ScheduledAt,
                CreatedAt = quickMatchProjection.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-creation setup failed for Match {MatchId}. Performing compensating cleanup.", quickMatchProjection.Id);

            // Roll back the newly created match entity to prevent leaving orphaned records
            await _matchRepository.DeleteAsync(quickMatchProjection.Id, CancellationToken.None);

            throw;
        }
    }
}