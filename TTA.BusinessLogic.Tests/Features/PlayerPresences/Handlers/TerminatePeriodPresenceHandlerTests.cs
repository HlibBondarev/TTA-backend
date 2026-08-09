using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.BusinessLogic.Features.PlayerPresences.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Handlers;

/// <summary>
/// Unit tests for the <see cref="TerminatePeriodPresenceHandler"/> class.
/// </summary>
public class TerminatePeriodPresenceHandlerTests
{
    private readonly Mock<IPlayerPresenceRepository> _playerPresenceRepositoryMock = new();
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<IMatchLineupRepository> _matchLineupRepositoryMock = new();
    private readonly Mock<ILogger<TerminatePeriodPresenceHandler>> _loggerMock = new();
    private readonly TerminatePeriodPresenceHandler _handler;

    public TerminatePeriodPresenceHandlerTests()
    {
        _handler = new TerminatePeriodPresenceHandler(
            _playerPresenceRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _matchLineupRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_Should_CloseActivePresences_When_MatchAndLineupIdsAreValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var lineupId1 = Guid.NewGuid();
        var lineupId2 = Guid.NewGuid();
        var command = new TerminatePeriodPresenceCommand(
            MatchId: matchId,
            PeriodNumber: 1,
            PlayerLineupIds: new[] { lineupId1, lineupId2 },
            TimeOut: DateTime.UtcNow);

        var match = new Match { Id = matchId };
        var validLineups = new List<MatchLineupProjection>
        {
            new(lineupId1, matchId, Guid.NewGuid(), Guid.NewGuid(), "John", "Doe", 1, Guid.NewGuid(), "CF"),
            new(lineupId2, matchId, Guid.NewGuid(), Guid.NewGuid(), "Jane", "Smith", 2, Guid.NewGuid(), "GK")
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _matchLineupRepositoryMock
            .Setup(r => r.GetMatchLineupsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validLineups);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _playerPresenceRepositoryMock.Verify(r => r.CloseActivePresencesAsync(
            command.MatchId,
            command.PeriodNumber,
            command.TimeOut,
            command.PlayerLineupIds,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ThrowNotFoundException_When_MatchDoesNotExist()
    {
        // Arrange
        var command = new TerminatePeriodPresenceCommand(
            MatchId: Guid.NewGuid(),
            PeriodNumber: 1,
            PlayerLineupIds: new[] { Guid.NewGuid() },
            TimeOut: DateTime.UtcNow);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {command.MatchId} was not found.");

        _playerPresenceRepositoryMock.Verify(r => r.CloseActivePresencesAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ThrowNotFoundException_When_RequestContainsInvalidOrCrossMatchLineupIds()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var validLineupId = Guid.NewGuid();
        var crossMatchLineupId = Guid.NewGuid();

        var command = new TerminatePeriodPresenceCommand(
            MatchId: matchId,
            PeriodNumber: 1,
            PlayerLineupIds: new[] { validLineupId, crossMatchLineupId },
            TimeOut: DateTime.UtcNow);

        var match = new Match { Id = matchId };
        var validLineups = new List<MatchLineupProjection>
        {
            new(validLineupId, matchId, Guid.NewGuid(), Guid.NewGuid(), "John", "Doe", 1, Guid.NewGuid(), "CF")
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _matchLineupRepositoryMock
            .Setup(r => r.GetMatchLineupsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validLineups);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"One or more specified player lineup IDs do not belong to Match {matchId}.");

        _playerPresenceRepositoryMock.Verify(r => r.CloseActivePresencesAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}