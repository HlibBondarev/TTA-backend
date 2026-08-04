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
/// Handles the execution of <see cref="CreateQuickMatchCommand"/> to provision JIT entities, assign access policies, 
/// create the match, and initialize starting lineups for both competing teams.
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
    /// Provisions quick match infrastructure, verifies or grants team editor access policies, 
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

        try
        {
            // 1. Just-In-Time (JIT) user provisioning in public.users
            var existingUser = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);
            if (existingUser == null)
            {
                isNewUser = true;
                _logger.LogInformation("User {UserId} not found in database. Provisioning JIT record.", command.UserId);
            }

            var userEntity = new User
            {
                Id = command.UserId,
                Email = command.UserEmail,
                DisplayName = command.UserName,
                CreatedAt = DateTime.UtcNow
            };

            await _userRepository.UpsertAsync(userEntity, cancellationToken);

            // 2. Atomic JIT infrastructure provisioning and Match entity creation via PostgreSQL stored function
            quickMatch = await _matchRepository.CreateQuickMatchAsync(
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

            // 3. Ensure TeamEditor access policy JIT for Home Squad
            var activePolicy = await _accessRepository.GetActiveTeamPolicyAsync(
                command.UserId,
                quickMatch.HomeTeamId,
                cancellationToken);

            if (activePolicy == null)
            {
                _logger.LogDebug("Granting TeamEditor policy for User {UserId} on HomeTeam {HomeTeamId}.",
                    command.UserId, quickMatch.HomeTeamId);

                var newPolicy = new AccessPolicy
                {
                    Id = Guid.NewGuid(),
                    UserId = command.UserId,
                    TargetType = TargetScope.Team,
                    TargetId = quickMatch.HomeTeamId,
                    Role = AppRole.Editor,
                    CreatedAt = DateTime.UtcNow
                };

                await _accessRepository.AddAccessAsync(newPolicy, cancellationToken);
            }

            // 4. Resolve target SportConfiguration ID
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

            // 5. Fetch Sport Configuration to obtain LineupLimit
            var sportConfig = await _sportConfigurationRepository.GetByIdAsync(targetConfigurationId, cancellationToken);
            if (sportConfig == null)
            {
                _logger.LogError("Sport configuration with ID {ConfigurationId} was not found.", targetConfigurationId);
                throw new KeyNotFoundException($"Sport configuration with ID {targetConfigurationId} was not found.");
            }

            // 6. Populate starting lineup for BOTH Home Squad and Guest Squad
            _logger.LogDebug("Populating starting lineups for Match {MatchId}.", quickMatch.Id);

            // Populate Home Squad
            var homeRoster = await _rosterRepository.GetTeamRosterAsync(
                quickMatch.TournamentId,
                quickMatch.HomeTeamId,
                cancellationToken);

            var homeStarters = homeRoster
                .Take(sportConfig.LineupLimit)
                .Select(r => (Guid)r.id)
                .ToArray();

            if (homeStarters.Length > 0)
            {
                await _matchLineupRepository.CopyFromRosterAsync(
                    quickMatch.Id,
                    quickMatch.HomeTeamId,
                    homeStarters,
                    cancellationToken);
            }

            // Populate Guest Squad
            var guestRoster = await _rosterRepository.GetTeamRosterAsync(
                quickMatch.TournamentId,
                quickMatch.GuestTeamId,
                cancellationToken);

            var guestStarters = guestRoster
                .Take(sportConfig.LineupLimit)
                .Select(r => (Guid)r.id)
                .ToArray();

            if (guestStarters.Length > 0)
            {
                await _matchLineupRepository.CopyFromRosterAsync(
                    quickMatch.Id,
                    quickMatch.GuestTeamId,
                    guestStarters,
                    cancellationToken);
            }

            _logger.LogInformation("Successfully completed quick match creation for Match {MatchId}.", quickMatch.Id);

            // 7. Map and return QuickMatchResponse DTO
            return new QuickMatchResponse
            {
                Id = quickMatch.Id,
                TournamentId = quickMatch.TournamentId,
                HomeTeamId = quickMatch.HomeTeamId,
                GuestTeamId = quickMatch.GuestTeamId,
                ScheduledAt = quickMatch.ScheduledAt,
                CreatedAt = quickMatch.CreatedAt
            };
        }
        catch
        {
            // Roll back created entities if quick match provisioning fails
            if (quickMatch != null)
            {
                await _matchRepository.DeleteAsync(quickMatch.Id, CancellationToken.None);
            }

            if (isNewUser)
            {
                await _userRepository.DeleteAsync(command.UserId, CancellationToken.None);
            }

            throw;
        }
    }
}