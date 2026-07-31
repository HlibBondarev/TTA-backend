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
/// Unit tests for <see cref="CreateQuickMatchCommandHandler"/>.
/// </summary>
public class CreateQuickMatchCommandHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<IAccessRepository> _accessRepositoryMock = new();
    private readonly Mock<ISportRepository> _sportRepositoryMock = new();
    private readonly Mock<ISportConfigurationRepository> _sportConfigurationRepositoryMock = new();
    private readonly Mock<IRosterRepository> _rosterRepositoryMock = new();
    private readonly Mock<IMatchLineupRepository> _matchLineupRepositoryMock = new();
    private readonly Mock<ILogger<CreateQuickMatchCommandHandler>> _loggerMock = new();

    private readonly CreateQuickMatchCommandHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateQuickMatchCommandHandlerTests"/> class and sets up dependencies.
    /// </summary>
    public CreateQuickMatchCommandHandlerTests()
    {
        _handler = new CreateQuickMatchCommandHandler(
            _matchRepositoryMock.Object,
            _accessRepositoryMock.Object,
            _sportRepositoryMock.Object,
            _sportConfigurationRepositoryMock.Object,
            _rosterRepositoryMock.Object,
            _matchLineupRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Helper method to create dynamic roster items mimicking Dapper dynamic rows.
    /// </summary>
    private static dynamic CreateMockRosterItem(Guid id)
    {
        IDictionary<string, object?> expando = new ExpandoObject();
        expando["id"] = id;
        return expando;
    }

    /// <summary>
    /// Verifies successful quick match creation when explicit ConfigurationId is provided and user does not have an active policy.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldCreateQuickMatch_WhenExplicitConfigurationIdProvided_AndUserHasNoPolicy()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var userId = "auth0|user123";
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = configId };
        var command = new CreateQuickMatchCommand(request, userId);

        var projection = new QuickMatchProjection
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

        var rosterItem1Id = Guid.NewGuid();
        var rosterItem2Id = Guid.NewGuid();
        var rosterItem3Id = Guid.NewGuid();

        // Using ExpandoObject instead of anonymous types for Dapper dynamic simulation
        var mockRoster = new List<dynamic>
        {
            CreateMockRosterItem(rosterItem1Id),
            CreateMockRosterItem(rosterItem2Id),
            CreateMockRosterItem(rosterItem3Id)
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
            .ReturnsAsync(mockRoster);

        _matchLineupRepositoryMock
            .Setup(l => l.CopyFromRosterAsync(projection.Id, projection.HomeTeamId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(projection.Id, result.Id);
        Assert.Equal(projection.TournamentId, result.TournamentId);
        Assert.Equal(projection.HomeTeamId, result.HomeTeamId);
        Assert.Equal(projection.GuestTeamId, result.GuestTeamId);

        _accessRepositoryMock.Verify(
            a => a.AddAccessAsync(
                It.Is<AccessPolicy>(p => p.UserId == userId && p.TargetId == projection.HomeTeamId && p.TargetType == TargetScope.Team && p.Role == AppRole.Editor),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _matchLineupRepositoryMock.Verify(
            l => l.CopyFromRosterAsync(
                projection.Id,
                projection.HomeTeamId,
                It.Is<IEnumerable<Guid>>(ids => ids.Count() == 2 && ids.Contains(rosterItem1Id) && ids.Contains(rosterItem2Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _sportRepositoryMock.Verify(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that DefaultConfigId from Sport is resolved when ConfigurationId is omitted in the request.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldResolveDefaultConfig_WhenConfigurationIdIsNull()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var defaultConfigId = Guid.NewGuid();
        var userId = "auth0|user123";
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = null };
        var command = new CreateQuickMatchCommand(request, userId);

        var projection = new QuickMatchProjection
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
            .Setup(r => r.GetTeamRosterAsync(projection.TournamentId, projection.HomeTeamId, It.IsAny<CancellationToken>()))
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
    /// Verifies that an <see cref="InvalidOperationException"/> is thrown when infrastructure provisioning returns null.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowInvalidOperationException_WhenProvisioningFails()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var request = new CreateQuickMatchRequest { SportId = sportId };
        var command = new CreateQuickMatchCommand(request, "auth0|user123");

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuickMatchProjection?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(sportId.ToString(), exception.Message);
    }

    /// <summary>
    /// Verifies that an <see cref="InvalidOperationException"/> is thrown when default sport resolution fails.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowInvalidOperationException_WhenSportNotFoundForDefaultConfig()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = null };
        var command = new CreateQuickMatchCommand(request, "auth0|user123");

        var projection = new QuickMatchProjection { Id = Guid.NewGuid(), HomeTeamId = Guid.NewGuid() };

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(It.IsAny<string>(), projection.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPolicy());

        _sportRepositoryMock
            .Setup(s => s.GetByIdAsync(sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sport?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(sportId.ToString(), exception.Message);
    }

    /// <summary>
    /// Verifies that an <see cref="InvalidOperationException"/> is thrown when sport configuration entity is not found.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowInvalidOperationException_WhenSportConfigurationNotFound()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var request = new CreateQuickMatchRequest { SportId = sportId, ConfigurationId = configId };
        var command = new CreateQuickMatchCommand(request, "auth0|user123");

        var projection = new QuickMatchProjection { Id = Guid.NewGuid(), HomeTeamId = Guid.NewGuid() };

        _matchRepositoryMock
            .Setup(r => r.CreateQuickMatchAsync(sportId, configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);

        _accessRepositoryMock
            .Setup(a => a.GetActiveTeamPolicyAsync(It.IsAny<string>(), projection.HomeTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPolicy());

        _sportConfigurationRepositoryMock
            .Setup(c => c.GetByIdAsync(configId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SportConfiguration?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains(configId.ToString(), exception.Message);
    }
}