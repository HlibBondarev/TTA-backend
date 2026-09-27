using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.BusinessLogic.Features.MatchLineups.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Handlers;

/// <summary>
/// Unit tests for the <see cref="CopyTeamRosterToMatchLineupHandler"/> ensures 
/// proper validation of match existence and correct delegation of bulk copy operations.
/// </summary>
public class CopyTeamRosterToMatchLineupHandlerTests
{
    private readonly Mock<IMatchLineupRepository> _matchLineupRepoMock;
    private readonly Mock<IMatchRepository> _matchRepoMock;
    private readonly Mock<ILogger<CopyTeamRosterToMatchLineupHandler>> _loggerMock;
    private readonly CopyTeamRosterToMatchLineupHandler _handler;

    public CopyTeamRosterToMatchLineupHandlerTests()
    {
        _matchLineupRepoMock = new Mock<IMatchLineupRepository>();
        _matchRepoMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<CopyTeamRosterToMatchLineupHandler>>();

        _handler = new CopyTeamRosterToMatchLineupHandler(
            _matchLineupRepoMock.Object,
            _matchRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler calls the repository with correct parameters and returns the count.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnInsertedCount()
    {
        // Arrange
        var playerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var command = new CopyTeamRosterToMatchLineupCommand(Guid.NewGuid(), Guid.NewGuid(), playerIds);
        const int expectedCount = 2;

        _matchRepoMock
            .Setup(x => x.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Match { Id = command.MatchId });

        _matchLineupRepoMock
            .Setup(x => x.CopyFromRosterAsync(command.MatchId, command.TeamId, command.PlayerRosterIds, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCount);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(expectedCount);
        _matchRepoMock.Verify(x => x.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()), Times.Once);
        _matchLineupRepoMock.Verify(x => x.CopyFromRosterAsync(command.MatchId, command.TeamId, command.PlayerRosterIds, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown if the specified match does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_NonExistentMatch_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new CopyTeamRosterToMatchLineupCommand(Guid.NewGuid(), Guid.NewGuid(), new List<Guid>());

        _matchRepoMock
            .Setup(x => x.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {command.MatchId} was not found.");

        _matchLineupRepoMock.Verify(x => x.CopyFromRosterAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}