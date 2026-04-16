using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Rosters.Commands;
using TTA.BusinessLogic.Features.Rosters.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Rosters.Handlers;

/// <summary>
/// Unit tests for the <see cref="RemovePlayerFromRosterHandler"/> ensuring correct validation 
/// of tournament state and proper repository invocation.
/// </summary>
public class RemovePlayerFromRosterHandlerTests
{
    private readonly Mock<IRosterRepository> _rosterRepoMock;
    private readonly Mock<ITournamentRepository> _tournamentRepoMock;
    private readonly Mock<ILogger<RemovePlayerFromRosterHandler>> _loggerMock;
    private readonly RemovePlayerFromRosterHandler _handler;

    public RemovePlayerFromRosterHandlerTests()
    {
        _rosterRepoMock = new Mock<IRosterRepository>();
        _tournamentRepoMock = new Mock<ITournamentRepository>();
        _loggerMock = new Mock<ILogger<RemovePlayerFromRosterHandler>>();

        _handler = new RemovePlayerFromRosterHandler(
            _rosterRepoMock.Object,
            _tournamentRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a player is successfully removed from the roster when the tournament is active.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_ShouldInvokeRepository()
    {
        // Arrange
        var command = new RemovePlayerFromRosterCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var tournament = new Tournament { Id = command.TournamentId, EndDate = DateTime.UtcNow.AddDays(1) };

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(command.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tournament);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _rosterRepoMock.Verify(x => x.RemovePlayerFromRosterAsync(
            command.TournamentId,
            command.TeamId,
            command.PlayerId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when the tournament does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_TournamentNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new RemovePlayerFromRosterCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(command.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament)null!);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*ID {command.TournamentId}*");

        _rosterRepoMock.Verify(x => x.RemovePlayerFromRosterAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that <see cref="BadRequestException"/> is thrown when attempting 
    /// to remove a player from a tournament that has already ended.
    /// </summary>
    [Fact]
    public async Task Handle_TournamentFinished_ShouldThrowBadRequestException()
    {
        // Arrange
        var command = new RemovePlayerFromRosterCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var finishedTournament = new Tournament { Id = command.TournamentId, EndDate = DateTime.UtcNow.AddDays(-1) };

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(command.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finishedTournament);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Cannot modify rosters of a finished tournament.");

        _rosterRepoMock.Verify(x => x.RemovePlayerFromRosterAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}