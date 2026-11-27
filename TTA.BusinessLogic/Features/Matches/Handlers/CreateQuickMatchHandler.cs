using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.Common.Enums;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles the execution of <see cref="CreateQuickMatchCommand"/> to create a match on top of the seeded quick match sandbox
/// (teams, JIT tournament and rosters), assign access policies for both Home and Guest teams,
/// initialize starting lineups for both competing teams, and automatically track the selected team context in the database.
/// Only the user record is provisioned just-in-time; the sandbox itself is seeded by the database scripts.
/// </summary>
/// <param name="userRepository">The user repository for JIT user provisioning and rollbacks.</param>
/// <param name="matchRepository">The match repository for database operations and JIT provisioning.</param>
/// <param name="accessRepository">The access repository for checking and granting team access policies.</param>
/// <param name="sportConfigurationRepository">The sport configuration repository for retrieving sport configuration rules.</param>
/// <param name="rosterRepository">The roster repository for fetching tournament rosters.</param>
/// <param name="matchLineupRepository">The match lineup repository for copying players into match lineups.</param>
/// <param name="logger">The logger instance for diagnostic messages.</param>
public class CreateQuickMatchHandler(
    IUserRepository userRepository,
    IMatchRepository matchRepository,
    IAccessRepository accessRepository,
    ISportConfigurationRepository sportConfigurationRepository,
    IRosterRepository rosterRepository,
    IMatchLineupRepository matchLineupRepository,
    ILogger<CreateQuickMatchHandler> logger) : IRequestHandler<CreateQuickMatchCommand, QuickMatchResponse>
{
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IAccessRepository _accessRepository = accessRepository;
    private readonly ISportConfigurationRepository _sportConfigurationRepository = sportConfigurationRepository;
    private readonly IRosterRepository _rosterRepository = rosterRepository;
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<CreateQuickMatchHandler> _logger = logger;

    /// <summary>PostgreSQL SQLSTATE raised when a required identifier is missing (null value not allowed).</summary>
    private const string SqlStateNullValueNotAllowed = "22004";

    /// <summary>Custom SQLSTATE raised when the sport configuration is missing or belongs to another sport.</summary>
    private const string SqlStateConfigurationNotFound = "P0005";

    /// <summary>Custom SQLSTATE raised when the quick match sandbox is not seeded for the sport configuration.</summary>
    private const string SqlStateSandboxNotProvisioned = "P0006";

    /// <summary>
    /// Creates the quick match on the seeded sandbox, verifies or grants team editor access policies for both competing teams, 
    /// and copies starter roster entries into the match lineup for both Home and Guest teams.
    /// Performs compensating cleanup if post-creation provisioning fails.
    /// </summary>
    /// <param name="command">The command containing quick match setup parameters and authenticated user details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="QuickMatchResponse"/> containing the newly created match details.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the quick match creation returns no match, or if the specified sport configuration is missing.</exception>
    /// <exception cref="NotFoundException">Thrown if the configuration does not exist or does not belong to the sport.</exception>
    /// <exception cref="ConflictException">Thrown if the quick match sandbox is not provisioned for the sport configuration.</exception>
    /// <exception cref="BadRequestException">Thrown if a required identifier is missing.</exception>
    public async Task<QuickMatchResponse> Handle(
        CreateQuickMatchCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Initiating quick match creation for MatchId {MatchId} and SportId {SportId} by User {UserId}.",
            command.Request.Id, command.Request.SportId, command.UserId);

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
    /// Invokes the sport-agnostic stored procedure to create the match entity on the seeded sandbox and track the team context.
    /// </summary>
    /// <param name="command">The command containing setup parameters.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The newly created <see cref="Match"/> entity.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if stored procedure returns null.</exception>
    /// <exception cref="NotFoundException">Thrown if the configuration does not exist or does not belong to the sport (P0005).</exception>
    /// <exception cref="ConflictException">Thrown if the quick match sandbox is not provisioned (P0006).</exception>
    /// <exception cref="BadRequestException">Thrown if a required identifier is missing (22004).</exception>
    private async Task<Match> CreateQuickMatchEntityAsync(CreateQuickMatchCommand command, CancellationToken cancellationToken)
    {
        Match? quickMatch;
        try
        {
            quickMatch = await _matchRepository.CreateQuickMatchAsync(
                command.Request.Id,
                command.Request.SportId,
                command.UserId,
                command.Request.ConfigurationId,
                command.Request.IsGuestTeam,
                cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == SqlStateConfigurationNotFound)
        {
            _logger.LogWarning(ex, "Quick match creation failed: configuration {ConfigurationId} was not found for sport {SportId}.",
                command.Request.ConfigurationId, command.Request.SportId);
            throw new NotFoundException("Sport configuration was not found or does not belong to the specified sport.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == SqlStateSandboxNotProvisioned)
        {
            _logger.LogError(ex, "Quick match sandbox is not provisioned for sport {SportId} and configuration {ConfigurationId}.",
                command.Request.SportId, command.Request.ConfigurationId);
            throw new ConflictException("Quick match sandbox is not provisioned for the specified sport and configuration.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == SqlStateNullValueNotAllowed)
        {
            _logger.LogWarning(ex, "Quick match creation failed: a required identifier is missing for match {MatchId}.", command.Request.Id);
            throw new BadRequestException("Match, user and configuration identifiers are required for quick match creation.", ex);
        }

        if (quickMatch == null)
        {
            _logger.LogError("Failed to create quick match for MatchId {MatchId} and SportId {SportId}.",
                command.Request.Id, command.Request.SportId);
            throw new KeyNotFoundException($"Failed to create quick match for SportId: {command.Request.SportId}");
        }

        _logger.LogInformation("Quick match {MatchId} created with HomeTeam {HomeTeamId} and GuestTeam {GuestTeamId}.",
            quickMatch.Id, quickMatch.HomeTeamId, quickMatch.GuestTeamId);

        return quickMatch;
    }

    /// <summary>
    /// Ensures the requesting user possesses an active TeamEditor (or higher) policy for the specified team.
    /// If an active policy exists with a Viewer role, it is promoted to TeamEditor and persisted via AddAccessAsync (upsert).
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
            if (IsEditorOrHigher(activePolicy.Role))
            {
                return null;
            }

            _logger.LogInformation("Promoting existing Viewer policy {PolicyId} to TeamEditor for User {UserId} on Team {TeamId}.",
                activePolicy.Id, userId, teamId);

            activePolicy.Role = AppRole.Editor;
            await _accessRepository.AddAccessAsync(activePolicy, cancellationToken);
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
    /// Determines whether the specified role provides Editor-level or higher access.
    /// </summary>
    /// <param name="role">The access role to evaluate.</param>
    /// <returns><c>true</c> if the role is Editor or FullControl; otherwise, <c>false</c>.</returns>
    private static bool IsEditorOrHigher(AppRole role) => role == AppRole.Editor || role == AppRole.FullControl;

    /// <summary>
    /// Resolves sport configuration rules and coordinates starting lineup population for both Home and Guest teams.
    /// </summary>
    /// <param name="quickMatch">The newly created match entity.</param>
    /// <param name="command">The quick match creation command.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="KeyNotFoundException">Thrown if sport configuration is not found.</exception>
    private async Task PopulateStartingLineupsAsync(Match quickMatch, CreateQuickMatchCommand command, CancellationToken cancellationToken)
    {
        var targetConfigId = command.Request.ConfigurationId;

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