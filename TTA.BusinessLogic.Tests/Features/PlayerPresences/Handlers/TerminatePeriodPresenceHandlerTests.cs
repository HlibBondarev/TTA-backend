using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.BusinessLogic.Features.PlayerPresences.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Handlers;

/// <summary>
/// Unit tests for the <see cref="TerminatePeriodPresenceHandler"/> class.
/// </summary>
public class TerminatePeriodPresenceHandlerTests
{
    private readonly Mock<IPlayerPresenceRepository> _playerPresenceRepositoryMock = new();
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<ILogger<TerminatePeriodPresenceHandler>> _loggerMock = new();
    private readonly TerminatePeriodPresenceHandler _handler;

    public TerminatePeriodPresenceHandlerTests()
    {
        _handler = new TerminatePeriodPresenceHandler(
            _playerPresenceRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_Should_CloseActivePresences_When_MatchExists()
    {
        // Arrange
        var lineupIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var command = new TerminatePeriodPresenceCommand(
            MatchId: Guid.NewGuid(),
            PeriodNumber: 1,
            PlayerLineupIds: lineupIds,
            TimeOut: DateTime.UtcNow);

        var match = new Match { Id = command.MatchId };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _playerPresenceRepositoryMock.Verify(r => r.CloseActivePresencesAsync(
            command.MatchId,
            command.PeriodNumber,
            command.TimeOut,
            lineupIds,
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
    }
}