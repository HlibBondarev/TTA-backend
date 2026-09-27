using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="CatchMatchHandler"/>.
/// </summary>
public class CatchMatchHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<ILogger<CatchMatchHandler>> _loggerMock = new();
    private readonly CatchMatchHandler _handler;

    public CatchMatchHandlerTests()
    {
        _handler = new CatchMatchHandler(_matchRepositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="CatchMatchHandler"/> throws <see cref="NotFoundException"/> 
    /// when the specified match does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenMatchDoesNotExist()
    {
        // Arrange
        var command = new CatchMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "user-123");

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {command.MatchId} was not found.");

        _matchRepositoryMock.Verify(r => r.CatchMatchAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that <see cref="CatchMatchHandler"/> successfully catches a match 
    /// and returns true when input is valid and match exists.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnTrue_WhenCatchMatchSucceeds()
    {
        // Arrange
        var command = new CatchMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "user-123");
        var existingMatch = new Match { Id = command.MatchId };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMatch);

        _matchRepositoryMock
            .Setup(r => r.CatchMatchAsync(command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        _matchRepositoryMock.Verify(r => r.CatchMatchAsync(
            command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="CatchMatchHandler"/> returns false when the match is already catched by the user.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnFalse_WhenMatchIsAlreadyCatched()
    {
        // Arrange
        var command = new CatchMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "user-123");
        var existingMatch = new Match { Id = command.MatchId };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMatch);

        _matchRepositoryMock
            .Setup(r => r.CatchMatchAsync(command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="CatchMatchHandler"/> throws <see cref="ConflictException"/> 
    /// when PostgreSQL raises business rule violation P0001 (e.g. team does not participate in match).
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenPostgresExceptionP0001IsRaised()
    {
        // Arrange
        var command = new CatchMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "user-123");
        var existingMatch = new Match { Id = command.MatchId };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMatch);

        var pgException = new PostgresException("Team does not participate in match.", "Severity", "InvariantSeverity", "P0001");

        _matchRepositoryMock
            .Setup(r => r.CatchMatchAsync(command.MatchId, command.TeamId, command.UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Team does not participate in match.");
    }
}