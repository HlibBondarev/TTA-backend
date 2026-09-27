using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="UncatchMatchHandler"/>.
/// </summary>
public class UncatchMatchHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<ILogger<UncatchMatchHandler>> _loggerMock = new();
    private readonly UncatchMatchHandler _handler;

    public UncatchMatchHandlerTests()
    {
        _handler = new UncatchMatchHandler(_matchRepositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="UncatchMatchHandler"/> throws <see cref="NotFoundException"/> 
    /// when user does not track the specified match/team context.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenMatchIsNotTrackedByUser()
    {
        // Arrange
        var command = new UncatchMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "user-123");

        _matchRepositoryMock
            .Setup(r => r.IsMatchCatchedByUserAsync(command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Tracking record for Match {command.MatchId} and Team {command.TeamId} was not found.");

        _matchRepositoryMock.Verify(r => r.UncatchMatchAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that <see cref="UncatchMatchHandler"/> successfully uncatches a match 
    /// and returns true when tracking record exists.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnTrue_WhenUncatchSucceeds()
    {
        // Arrange
        var command = new UncatchMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "user-123");

        _matchRepositoryMock
            .Setup(r => r.IsMatchCatchedByUserAsync(command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _matchRepositoryMock
            .Setup(r => r.UncatchMatchAsync(command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        _matchRepositoryMock.Verify(r => r.UncatchMatchAsync(
            command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()), Times.Once);
    }
}