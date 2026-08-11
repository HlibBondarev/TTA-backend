using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.Common.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="CreateQuickMatchHandler"/>.
/// </summary>
/// <remarks>
/// Validates business rules and orchestration logic during quick match creation, including:
/// <list type="bullet">
///   <item><description>Just-in-time user provisioning and registration in database.</description></item>
///   <item><description>Atomic just-in-time infrastructure provisioning via database repositories.</description></item>
///   <item><description>Automated granting of team editor permissions for both Home and Guest team managers.</description></item>
///   <item><description>Fallback resolution to default sport configurations when config IDs are omitted.</description></item>
///   <item><description>Initial starting lineup population for both Home and Guest teams up to configured lineup limits.</description></item>
///   <item><description>Compensating rollback cleanup when post-creation initialization steps fail.</description></item>
/// </list>
/// </remarks>
public class CreateQuickMatchHandlerTests
{
    /// <summary>
    /// Mock instance for managing user JIT registration and compensating deletions.
    /// </summary>
    private readonly Mock<IUserRepository> _userRepositoryMock = new();

    /// <summary>
    /// Mock instance for managing match persistence and invoking underlying stored procedures.
    /// </summary>
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();

    /// <summary>
    /// Mock instance for evaluating active team permissions and granting JIT access policies.
    /// </summary>
    private readonly Mock<IAccessRepository> _accessRepositoryMock = new();

    /// <summary>
    /// Mock instance for retrieving sport discipline metadata and default configuration identifiers.
    /// </summary>
    private readonly Mock<ISportRepository> _sportRepositoryMock = new();

    /// <summary>
    /// Mock instance for fetching sport configuration rules and lineup limits.
    /// </summary>
    private readonly Mock<ISportConfigurationRepository> _sportConfigurationRepositoryMock = new();

    /// <summary>
    /// Mock instance for fetching team tournament roster records.
    /// </summary>
    private readonly Mock<IRosterRepository> _rosterRepositoryMock = new();

    /// <summary>
    /// Mock instance for executing bulk copy operations into match starting lineups.
    /// </summary>
    private readonly Mock<IMatchLineupRepository> _matchLineupRepositoryMock = new();

    /// <summary>
    /// Mock logger instance for verifying diagnostic logging and error context output.
    /// </summary>
    private readonly Mock<ILogger<CreateQuickMatchHandler>> _loggerMock = new();

    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private readonly CreateQuickMatchHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateQuickMatchHandlerTests"/> class,
    /// setting up mocked dependencies and instantiating the handler under test.
    /// </summary>
    public CreateQuickMatchHandlerTests()
    {
        _handler = new CreateQuickMatchHandler(
            _userRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _accessRepositoryMock.Object,
            _sportRepositoryMock.Object,
            _sportConfigurationRepositoryMock.Object,
            _rosterRepositoryMock.Object,
            _matchLineupRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Helper factory method to construct dynamic roster item objects mimicking Dapper query projections.
    /// </summary>
    /// <param name="id">The unique identifier of the target player roster entry.</param>
    /// <returns>A dynamic <see cref="ExpandoObject"/> holding the roster identifier.</returns>
    private static dynamic CreateMockRosterItem(Guid id)
    {
        IDictionary<string, object?> expando = new ExpandoObject();
        expando["id"] = id;
        return expando;
    }

    /// <summary>
    /// Verifies successful quick match creation and starting lineup population for both Home and Guest squads
    /// when an explicit configuration ID is supplied and the requesting user holds no prior team policy for either team.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldCreateQuickMatchAndGrantPoliciesAndPopulateLineupsForBothTeams_WhenRequestIsValid()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        const string userId = "auth0|user123";
        const string userEmail = "user123@example.com";
        const string userName = "Test User";

        var request = new CreateQuickMatchRequest(sportId, configId);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        var createdMatch = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sportConfig = new SportConfiguration
        {
            Id = configId,
            SportId = sportId,
            LineupLimit = 2
        };

        // Arrange Home Squad Roster (3 players available, limit is 2)
        var homeRoster1Id = Guid.NewGuid();
        var homeRoster2Id = Guid.NewGuid();
        var homeRoster3Id = Guid.NewGuid();
        var mockHomeRoster = new List<dynamic>
        {
            CreateMockRosterItem(homeRoster1Id),
            CreateMockRosterItem(homeRoster2Id),
            CreateMockRosterItem(homeRoster3Id)
        };

        // Arrange Guest Squad Roster (2 players available)
        var guestRoster1Id = Guid.NewGuid();
        var guestRoster2Id = Guid.NewGuid();
        var mockGuestRoster = new List<dynamic>
        {
            CreateMockRosterItem(guestRoster1Id),
            CreateMockRosterItem(guestRoster2Id)
        };

        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, false));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdMatch);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, createdMatch.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, createdMatch.GuestTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sportConfig);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(createdMatch.TournamentId, createdMatch.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHomeRoster);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(createdMatch.TournamentId, createdMatch.GuestTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGuestRoster);

        _matchLineupRepositoryMock
            .Setup(l => l.CopyFromRosterAsync(createdMatch.Id, It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(createdMatch.Id, result.Id);
        Assert.Equal(createdMatch.TournamentId, result.TournamentId);
        Assert.Equal(createdMatch.HomeTeamId, result.HomeTeamId);
        Assert.Equal(createdMatch.GuestTeamId, result.GuestTeamId);

        // Verify TeamEditor access policy grant for Home Team
        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == createdMatch.HomeTeamId && p.TargetType == TargetScope.Team && p.Role == AppRole.Editor),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify TeamEditor access policy grant for Guest Team
        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == createdMatch.GuestTeamId && p.TargetType == TargetScope.Team && p.Role == AppRole.Editor),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify CopyFromRosterAsync for Home Team (sliced to LineupLimit = 2)
        _matchLineupRepositoryMock.Verify(
            l => l.CopyFromRosterAsync(
                createdMatch.Id,
                createdMatch.HomeTeamId,
                It.Is<IEnumerable<Guid>>(ids => ids.Count() == 2 && ids.Contains(homeRoster1Id) && ids.Contains(homeRoster2Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify CopyFromRosterAsync for Guest Team (sliced to LineupLimit = 2)
        _matchLineupRepositoryMock.Verify(
            l => l.CopyFromRosterAsync(
                createdMatch.Id,
                createdMatch.GuestTeamId,
                It.Is<IEnumerable<Guid>>(ids => ids.Count() == 2 && ids.Contains(guestRoster1Id) && ids.Contains(guestRoster2Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _sportRepositoryMock.Verify(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that policy grant is skipped for a team when an active Editor policy already exists for that team,
    /// but granted for the team that lacks an active policy.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldGrantPolicyOnlyForGuestTeam_WhenUserAlreadyHasHomeTeamPolicy()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        const string userId = "auth0|user123";
        const string userEmail = "user123@example.com";
        const string userName = "Test User";

        var request = new CreateQuickMatchRequest(sportId, configId);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        var createdMatch = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sportConfig = new SportConfiguration { Id = configId, SportId = sportId, LineupLimit = 7 };

        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, false));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdMatch);

        // User already has active Editor policy for Home Team
        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, createdMatch.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPolicy { UserId = userId, TargetId = createdMatch.HomeTeamId, Role = AppRole.Editor });

        // User does not have active policy for Guest Team
        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, createdMatch.GuestTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sportConfig);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(createdMatch.TournamentId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);

        // Verify AddAccessAsync is invoked ONLY ONCE for Guest Team
        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == createdMatch.GuestTeamId && p.TargetType == TargetScope.Team && p.Role == AppRole.Editor),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.TargetId == createdMatch.HomeTeamId),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a new TeamEditor policy is granted when the user only holds a Viewer-level policy for a team.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldGrantEditorPolicy_WhenUserHasOnlyViewerPolicy()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        const string userId = "auth0|user123";
        const string userEmail = "user123@example.com";
        const string userName = "Test User";

        var request = new CreateQuickMatchRequest(sportId, configId);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        var createdMatch = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sportConfig = new SportConfiguration { Id = configId, SportId = sportId, LineupLimit = 7 };

        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, false));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdMatch);

        // User holds only Viewer policy for Home Team
        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, createdMatch.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPolicy { UserId = userId, TargetId = createdMatch.HomeTeamId, Role = AppRole.Viewer });

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, createdMatch.GuestTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sportConfig);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(createdMatch.TournamentId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);

        // Verify AddAccessAsync is invoked for Home Team with Editor role despite having Viewer policy
        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == createdMatch.HomeTeamId && p.Role == AppRole.Editor),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify AddAccessAsync is also invoked for Guest Team with Editor role
        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == createdMatch.GuestTeamId && p.Role == AppRole.Editor),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that JIT user provisioning is executed when the requesting user record does not exist in the database.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldProvisionJitUser_WhenUserDoesNotExist()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        const string userId = "auth0|newuser123";
        const string userEmail = "newuser123@example.com";
        const string userName = "New User";

        var request = new CreateQuickMatchRequest(sportId, configId);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        var createdMatch = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sportConfig = new SportConfiguration { Id = configId, SportId = sportId, LineupLimit = 7 };

        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, true));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdMatch);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPolicy { Role = AppRole.Editor });

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sportConfig);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(createdMatch.TournamentId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);

        _userRepositoryMock.Verify(
            u => u.UpsertAsync(
                It.Is<User>(user => user.Id == userId && user.Email == userEmail && user.DisplayName == userName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the default sport configuration identifier is resolved from the target sport entity
    /// when <see cref="CreateQuickMatchRequest.ConfigurationId"/> is omitted or null in the incoming payload.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldResolveDefaultConfig_WhenConfigurationIdIsNull()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var defaultConfigId = Guid.NewGuid();
        const string userId = "auth0|user123";
        const string userEmail = "user123@example.com";
        const string userName = "Test User";

        var request = new CreateQuickMatchRequest(sportId, null);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        var createdMatch = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sport = new Sport { Id = sportId, Name = "Water Polo", ShortName = "WP", DefaultConfigId = defaultConfigId };
        var sportConfig = new SportConfiguration { Id = defaultConfigId, SportId = sportId, LineupLimit = 13 };

        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, false));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdMatch);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPolicy { Role = AppRole.Editor });

        _sportRepositoryMock
            .Setup(s => s.GetByIdAsync(sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sport);

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(defaultConfigId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sportConfig);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(createdMatch.TournamentId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        _sportRepositoryMock.Verify(s => s.GetByIdAsync(sportId, It.IsAny<CancellationToken>()), Times.Once);
        _sportConfigurationRepositoryMock.Verify(c => c.GetByIdAsync(defaultConfigId, It.IsAny<CancellationToken>()), Times.Once);
        _accessRepositoryMock.Verify(a => a.AddAccessAsync(It.IsAny<AccessPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
        _matchLineupRepositoryMock.Verify(l => l.CopyFromRosterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="KeyNotFoundException"/> is thrown when database-level quick match provisioning returns null,
    /// indicating an infrastructure or stored procedure execution failure.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowKeyNotFoundException_WhenProvisioningFails()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        const string userId = "auth0|user123";
        const string userEmail = "user123@example.com";
        const string userName = "Test User";

        var request = new CreateQuickMatchRequest(sportId);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, false));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(sportId.ToString(), exception.Message);
    }

    /// <summary>
    /// Verifies that a <see cref="KeyNotFoundException"/> is thrown when fallback sport resolution fails,
    /// and that a compensating rollback deletion is dispatched to purge the provisioned match entity, 
    /// both newly created access policies (verified per captured ID), and the newly provisioned user.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowKeyNotFoundExceptionAndRollbackNewUserAndBothPolicies_WhenSportNotFoundForDefaultConfig()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        const string userId = "auth0|user123";
        const string userEmail = "user123@example.com";
        const string userName = "Test User";

        var request = new CreateQuickMatchRequest(sportId, null);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        var createdMatch = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var createdPolicyIds = new List<Guid>();

        // User is newly provisioned -> UpsertAsync returns IsInserted = true
        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, true));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdMatch);

        // No prior active policy exists for either team
        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _accessRepositoryMock
            .Setup(a => a.AddAccessAsync(It.IsAny<AccessPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<AccessPolicy, CancellationToken>((policy, _) => createdPolicyIds.Add(policy.Id));

        _sportRepositoryMock
            .Setup(s => s.GetByIdAsync(sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sport?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(sportId.ToString(), exception.Message);

        // Verify match rollback
        _matchRepositoryMock.Verify(
            m => m.DeleteAsync(createdMatch.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify access policy rollback for both created policies (Home & Guest teams) by captured ID
        Assert.Equal(2, createdPolicyIds.Count);
        _accessRepositoryMock.Verify(
            a => a.DeleteAsync(createdPolicyIds[0], It.IsAny<CancellationToken>()),
            Times.Once);
        _accessRepositoryMock.Verify(
            a => a.DeleteAsync(createdPolicyIds[1], It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify JIT user rollback
        _userRepositoryMock.Verify(
            u => u.DeleteAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="KeyNotFoundException"/> is thrown when the target sport configuration entity is missing,
    /// and that compensating rollback triggers deletion of the created match entity and both created access policies (verified per captured ID), without deleting an existing user.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowKeyNotFoundExceptionAndNotDeleteExistingUser_WhenSportConfigurationNotFound()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        const string userId = "auth0|user123";
        const string userEmail = "user123@example.com";
        const string userName = "Test User";

        var request = new CreateQuickMatchRequest(sportId, configId);
        var command = new CreateQuickMatchCommand(request, userId, userEmail, userName);

        var createdMatch = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var createdPolicyIds = new List<Guid>();

        // User already existed prior to request -> UpsertAsync returns IsInserted = false
        _userRepositoryMock
            .Setup(u => u.UpsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => (user, false));

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, userId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdMatch);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _accessRepositoryMock
            .Setup(a => a.AddAccessAsync(It.IsAny<AccessPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<AccessPolicy, CancellationToken>((policy, _) => createdPolicyIds.Add(policy.Id));

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SportConfiguration?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(configId.ToString(), exception.Message);

        // Verify match rollback
        _matchRepositoryMock.Verify(
            m => m.DeleteAsync(createdMatch.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify access policy rollback for both Home and Guest team policies by captured ID
        Assert.Equal(2, createdPolicyIds.Count);
        _accessRepositoryMock.Verify(
            a => a.DeleteAsync(createdPolicyIds[0], It.IsAny<CancellationToken>()),
            Times.Once);
        _accessRepositoryMock.Verify(
            a => a.DeleteAsync(createdPolicyIds[1], It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify existing user is NOT deleted
        _userRepositoryMock.Verify(
            u => u.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}