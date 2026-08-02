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
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="CreateQuickMatchHandler"/>.
/// </summary>
/// <remarks>
/// Validates business rules and orchestration logic during quick match creation, including:
/// <list type="bullet">
///   <item><description>Atomic just-in-time infrastructure provisioning via database repositories.</description></item>
///   <item><description>Automated granting of team editor permissions for home squad managers.</description></item>
///   <item><description>Fallback resolution to default sport configurations when config IDs are omitted.</description></item>
///   <item><description>Initial starting lineup population for both Home and Guest teams up to configured lineup limits.</description></item>
///   <item><description>Compensating rollback cleanup when post-creation initialization steps fail.</description></item>
/// </list>
/// </remarks>
public class CreateQuickMatchHandlerTests
{
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
    /// when an explicit configuration ID is supplied and the requesting user holds no prior team policy.
    /// </summary>
    /// <remarks>
    /// Ensures that:
    /// <list type="number">
    ///   <item><description>Quick match infrastructure projection is fetched correctly.</description></item>
    ///   <item><description>TeamEditor access policy is granted to the requesting user for the Home Team.</description></item>
    ///   <item><description>Rosters for both Home and Guest squads are fetched and sliced according to <see cref="SportConfiguration.LineupLimit"/>.</description></item>
    ///   <item><description>The command returns a populated <see cref="QuickMatchResponse"/> DTO.</description></item>
    /// </list>
    /// </remarks>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test execution.</returns>
    [Fact]
    public async Task Handle_ShouldCreateQuickMatchAndPopulateLineupsForBothTeams_WhenRequestIsValid()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var userId = "auth0|user123";
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = configId };
        var command = new CreateQuickMatchCommand(request, userId);

        var projection = new QuickMatchProjection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow);

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

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, projection.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sportConfig);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(projection.TournamentId, projection.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHomeRoster);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(projection.TournamentId, projection.GuestTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGuestRoster);

        _matchLineupRepositoryMock
            .Setup(l => l.CopyFromRosterAsync(projection.Id, It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(projection.Id, result.Id);
        Assert.Equal(projection.TournamentId, result.TournamentId);
        Assert.Equal(projection.HomeTeamId, result.HomeTeamId);
        Assert.Equal(projection.GuestTeamId, result.GuestTeamId);

        // Verify TeamEditor access policy grant for Home Team
        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == projection.HomeTeamId && p.TargetType == TargetScope.Team && p.Role == AppRole.Editor),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify CopyFromRosterAsync for Home Team (sliced to LineupLimit = 2)
        _matchLineupRepositoryMock.Verify(
            l => l.CopyFromRosterAsync(
                projection.Id,
                projection.HomeTeamId,
                It.Is<IEnumerable<Guid>>(ids => ids.Count() == 2 && ids.Contains(homeRoster1Id) && ids.Contains(homeRoster2Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify CopyFromRosterAsync for Guest Team (sliced to LineupLimit = 2)
        _matchLineupRepositoryMock.Verify(
            l => l.CopyFromRosterAsync(
                projection.Id,
                projection.GuestTeamId,
                It.Is<IEnumerable<Guid>>(ids => ids.Count() == 2 && ids.Contains(guestRoster1Id) && ids.Contains(guestRoster2Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _sportRepositoryMock.Verify(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the default sport configuration identifier is resolved from the target sport entity
    /// when <see cref="CreateQuickMatchRequest.ConfigurationId"/> is omitted or null in the incoming payload.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test execution.</returns>
    [Fact]
    public async Task Handle_ShouldResolveDefaultConfig_WhenConfigurationIdIsNull()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var defaultConfigId = Guid.NewGuid();
        var userId = "auth0|user123";
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = null };
        var command = new CreateQuickMatchCommand(request, userId);

        var projection = new QuickMatchProjection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow);

        var sport = new Sport { Id = sportId, Name = "Water Polo", ShortName = "WP", DefaultConfigId = defaultConfigId };
        var sportConfig = new SportConfiguration { Id = defaultConfigId, SportId = sportId, LineupLimit = 13 };

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, projection.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPolicy());

        _sportRepositoryMock
            .Setup(s => s.GetByIdAsync(sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sport);

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(defaultConfigId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sportConfig);

        _rosterRepositoryMock
            .Setup(r => r.GetTeamRosterAsync(projection.TournamentId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test execution.</returns>
    [Fact]
    public async Task Handle_ShouldThrowKeyNotFoundException_WhenProvisioningFails()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var request = new CreateQuickMatchRequest { SportId = sportId };
        var command = new CreateQuickMatchCommand(request, "auth0|user123");

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuickMatchProjection?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(sportId.ToString(), exception.Message);
    }

    /// <summary>
    /// Verifies that a <see cref="KeyNotFoundException"/> is thrown when fallback sport resolution fails,
    /// and that a compensating rollback deletion is dispatched to purge the provisioned match entity.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test execution.</returns>
    [Fact]
    public async Task Handle_ShouldThrowKeyNotFoundException_WhenSportNotFoundForDefaultConfig()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var userId = "auth0|user123";
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = null };
        var command = new CreateQuickMatchCommand(request, userId);

        var projection = new QuickMatchProjection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow);

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, projection.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _sportRepositoryMock
            .Setup(s => s.GetByIdAsync(sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sport?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(sportId.ToString(), exception.Message);

        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == projection.HomeTeamId), It.IsAny<CancellationToken>()),
            Times.Once);

        _matchRepositoryMock.Verify(
            m => m.DeleteAsync(projection.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="KeyNotFoundException"/> is thrown when the target sport configuration entity is missing,
    /// and that compensating rollback triggers deletion of the created match entity.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test execution.</returns>
    [Fact]
    public async Task Handle_ShouldThrowKeyNotFoundException_WhenSportConfigurationNotFound()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var userId = "auth0|user123";
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = configId };
        var command = new CreateQuickMatchCommand(request, userId);

        var projection = new QuickMatchProjection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow);

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(userId, projection.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SportConfiguration?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(configId.ToString(), exception.Message);

        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == projection.HomeTeamId), It.IsAny<CancellationToken>()),
            Times.Once);

        _matchRepositoryMock.Verify(
            m => m.DeleteAsync(projection.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}