using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.Common.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the execution of <see cref="CreateQuickMatchCommand"/> to provision JIT entities, assign access policies 
/// for both Home and Guest teams, create the match, and initialize starting lineups for both competing teams.
/// </summary>
/// <param name="userRepository">The user repository for JIT user provisioning and rollbacks.</param>
/// <param name="matchRepository">The match repository for database operations and JIT provisioning.</param>
/// <param name="accessRepository">The access repository for checking and granting team access policies.</param>
/// <param name="sportRepository">The sport repository for retrieving sport metadata and default configurations.</param>
/// <param name="sportConfigurationRepository">The sport configuration repository for retrieving sport configuration rules.</param>
/// <param name="rosterRepository">The roster repository for fetching tournament rosters.</param>
/// <param name="matchLineupRepository">The match lineup repository for copying players into match lineups.</param>
/// <param name="logger">The logger instance for diagnostic messages.</param>
public class CreateQuickMatchHandler(
    IUserRepository userRepository,
    IMatchRepository matchRepository,
    IAccessRepository accessRepository,
    ISportRepository sportRepository,
    ISportConfigurationRepository sportConfigurationRepository,
    IRosterRepository rosterRepository,
    IMatchLineupRepository matchLineupRepository,
    ILogger<CreateQuickMatchHandler> logger) : IRequestHandler<CreateQuickMatchCommand, QuickMatchResponse>
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IAccessRepository _accessRepository = accessRepository;
    private readonly ISportRepository _sportRepository = sportRepository;
    private readonly ISportConfigurationRepository _sportConfigurationRepository = sportConfigurationRepository;
    private readonly IRosterRepository _rosterRepository = rosterRepository;
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<CreateQuickMatchHandler> _logger = logger;

    /// <summary>
    /// Provisions quick match infrastructure, verifies or grants team editor access policies for both competing teams, 
    /// and copies starter roster entries into the match lineup for both Home and Guest teams.
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

        var isNewUser = false;
        Match? quickMatch = null;
        var createdPolicyIds = new List<Guid>();

        try
        {
            isNewUser = await ProvisionJitUserAsync(command, cancellationToken);
            quickMatch = await CreateQuickMatchEntityAsync(command, cancellationToken);

            var homePolicyId = await EnsureTeamEditorPolicyAsync(command.UserId, quickMatch.HomeTeamId, cancellationToken);
            if (homePolicyId.HasValue)
            {
                createdPolicyIds.Add(homePolicyId.Value);
            }

            var guestPolicyId = await EnsureTeamEditorPolicyAsync(command.UserId, quickMatch.GuestTeamId, cancellationToken);
            if (guestPolicyId.HasValue)
            {
                createdPolicyIds.Add(guestPolicyId.Value);
            }

            await PopulateStartingLineupsAsync(quickMatch, command, cancellationToken);

            _logger.LogInformation("Successfully completed quick match creation for Match {MatchId}.", quickMatch.Id);

            return MapToResponse(quickMatch);
        }
        catch
        {
            await RollbackOnFailureAsync(quickMatch?.Id, createdPolicyIds, isNewUser ? command.UserId : null);
            throw;
        }
    }

    /// <summary>
    /// Attempts to provision a JIT user record using atomic upsert, returning true only if a new row was inserted.
    /// </summary>
    /// <param name="command">The command containing user claim details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><c>true</c> if a new user record was newly provisioned by this request; otherwise, <c>false</c>.</returns>
    private async Task<bool> ProvisionJitUserAsync(CreateQuickMatchCommand command, CancellationToken cancellationToken)
    {
        var userEntity = new User
        {
            Id = command.UserId,
            Email = command.UserEmail,
            DisplayName = command.UserName,
            CreatedAt = DateTime.UtcNow
        };

        var (_, isInserted) = await _userRepository.UpsertAsync(userEntity, cancellationToken);

        if (isInserted)
        {
            _logger.LogInformation("Provisioned new JIT record for user {UserId}.", command.UserId);
        }

        return isInserted;
    }

    /// <summary>
    /// Invokes the stored procedure to provision quick match infrastructure and create the match entity.
    /// </summary>
    /// <param name="command">The command containing setup parameters.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The newly created <see cref="Match"/> entity.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if stored procedure returns null.</exception>
    private async Task<Match> CreateQuickMatchEntityAsync(CreateQuickMatchCommand command, CancellationToken cancellationToken)
    {
        var quickMatch = await _matchRepository.CreateQuickMatchAsync(
            command.Request.SportId,
            command.UserId,
            command.Request.ConfigurationId,
            cancellationToken);

        if (quickMatch == null)
        {
            _logger.LogError("Failed to provision quick match infrastructure for SportId {SportId}.", command.Request.SportId);
            throw new KeyNotFoundException($"Failed to provision quick match infrastructure for SportId: {command.Request.SportId}");
        }

        _logger.LogInformation("Quick match {MatchId} created with HomeTeam {HomeTeamId} and GuestTeam {GuestTeamId}.",
            quickMatch.Id, quickMatch.HomeTeamId, quickMatch.GuestTeamId);

        return quickMatch;
    }

    /// <summary>
    /// Ensures the requesting user possesses an active TeamEditor policy for the specified team.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The unique identifier of the newly created access policy if one was granted; otherwise, <c>null</c>.</returns>
    private async Task<Guid?> EnsureTeamEditorPolicyAsync(string userId, Guid teamId, CancellationToken cancellationToken)
    {
        var activePolicy = await _accessRepository.GetActiveTeamPolicyAsync(userId, teamId, cancellationToken);
        if (activePolicy != null)
        {
            return null;
        }

        _logger.LogDebug("Granting TeamEditor policy for User {UserId} on Team {TeamId}.", userId, teamId);

        var newPolicy = new AccessPolicy
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetType = TargetScope.Team,
            TargetId = teamId,
            Role = AppRole.Editor,
            CreatedAt = DateTime.UtcNow
        };

        await _accessRepository.AddAccessAsync(newPolicy, cancellationToken);
        return newPolicy.Id;
    }

    /// <summary>
    /// Resolves sport configuration rules and coordinates starting lineup population for both Home and Guest teams.
    /// </summary>
    /// <param name="quickMatch">The newly created match entity.</param>
    /// <param name="command">The quick match creation command.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="KeyNotFoundException">Thrown if sport or sport configuration is not found.</exception>
    private async Task PopulateStartingLineupsAsync(Match quickMatch, CreateQuickMatchCommand command, CancellationToken cancellationToken)
    {
        var targetConfigId = await ResolveConfigurationIdAsync(command.Request.SportId, command.Request.ConfigurationId, cancellationToken);

        var sportConfig = await _sportConfigurationRepository.GetByIdAsync(targetConfigId, cancellationToken);
        if (sportConfig == null)
        {
            _logger.LogError("Sport configuration with ID {ConfigurationId} was not found.", targetConfigId);
            throw new KeyNotFoundException($"Sport configuration with ID {targetConfigId} was not found.");
        }

        _logger.LogDebug("Populating starting lineups for Match {MatchId}.", quickMatch.Id);

        await PopulateTeamStartersAsync(quickMatch.Id, quickMatch.TournamentId, quickMatch.HomeTeamId, sportConfig.LineupLimit, cancellationToken);
        await PopulateTeamStartersAsync(quickMatch.Id, quickMatch.TournamentId, quickMatch.GuestTeamId, sportConfig.LineupLimit, cancellationToken);
    }

    /// <summary>
    /// Resolves the effective sport configuration ID, falling back to the sport's default configuration if not specified.
    /// </summary>
    /// <param name="sportId">The unique identifier of the sport.</param>
    /// <param name="configurationId">The optional requested configuration identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The resolved configuration identifier.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if sport is missing.</exception>
    private async Task<Guid> ResolveConfigurationIdAsync(Guid sportId, Guid? configurationId, CancellationToken cancellationToken)
    {
        if (configurationId.HasValue && configurationId.Value != Guid.Empty)
        {
            return configurationId.Value;
        }

        var sport = await _sportRepository.GetByIdAsync(sportId, cancellationToken);
        if (sport == null)
        {
            _logger.LogError("Sport with ID {SportId} was not found.", sportId);
            throw new KeyNotFoundException($"Sport with ID {sportId} was not found.");
        }

        return sport.DefaultConfigId;
    }

    /// <summary>
    /// Fetches roster entries for a specific team and copies starter players into the match lineup up to the lineup limit.
    /// </summary>
    /// <param name="matchId">The unique identifier of the match.</param>
    /// <param name="tournamentId">The unique identifier of the tournament.</param>
    /// <param name="teamId">The unique identifier of the team.</param>
    /// <param name="lineupLimit">The maximum number of players allowed in the match lineup.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    private async Task PopulateTeamStartersAsync(Guid matchId, Guid tournamentId, Guid teamId, int lineupLimit, CancellationToken cancellationToken)
    {
        var roster = await _rosterRepository.GetTeamRosterAsync(tournamentId, teamId, cancellationToken);
        var starters = roster.Take(lineupLimit).Select(r => (Guid)r.id).ToArray();

        if (starters.Length > 0)
        {
            await _matchLineupRepository.CopyFromRosterAsync(matchId, teamId, starters, cancellationToken);
        }
    }

    /// <summary>
    /// Executes isolated compensating cleanup steps to delete created match, access policy entities, and user entities if provisioning fails.
    /// </summary>
    /// <param name="matchId">The optional match identifier to delete.</param>
    /// <param name="accessPolicyIds">The collection of access policy identifiers created during this request to purge.</param>
    /// <param name="newUserId">The optional user identifier to delete if provisioned during this request.</param>
    private async Task RollbackOnFailureAsync(Guid? matchId, IEnumerable<Guid> accessPolicyIds, string? newUserId)
    {
        if (matchId.HasValue)
        {
            try
            {
                await _matchRepository.DeleteAsync(matchId.Value, CancellationToken.None);
            }
            catch
            {
                // Suppress rollback exception to preserve the original business exception
            }
        }

        foreach (var policyId in accessPolicyIds)
        {
            try
            {
                await _accessRepository.DeleteAsync(policyId, CancellationToken.None);
            }
            catch
            {
                // Suppress rollback exception
            }
        }

        if (!string.IsNullOrEmpty(newUserId))
        {
            try
            {
                await _userRepository.DeleteAsync(newUserId, CancellationToken.None);
            }
            catch
            {
                // Suppress rollback exception
            }
        }
    }

    /// <summary>
    /// Maps a <see cref="Match"/> domain entity to a <see cref="QuickMatchResponse"/> DTO.
    /// </summary>
    /// <param name="quickMatch">The created match entity.</param>
    /// <returns>A populated <see cref="QuickMatchResponse"/> object.</returns>
    private static QuickMatchResponse MapToResponse(Match quickMatch) => new
    (
        quickMatch.Id,
        quickMatch.TournamentId,
        quickMatch.HomeTeamId,
        quickMatch.GuestTeamId,
        quickMatch.ScheduledAt,
        quickMatch.CreatedAt
    );
}