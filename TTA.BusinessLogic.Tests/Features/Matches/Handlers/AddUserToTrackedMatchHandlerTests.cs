using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="AddUserToTrackedMatchHandler"/>.
/// </summary>
public class AddUserToTrackedMatchHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ILogger<AddUserToTrackedMatchHandler>> _loggerMock = new();
    private readonly AddUserToTrackedMatchHandler _handler;

    public AddUserToTrackedMatchHandlerTests()
    {
        _handler = new AddUserToTrackedMatchHandler(
            _matchRepositoryMock.Object,
            _userRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="AddUserToTrackedMatchHandler"/> throws <see cref="ConflictException"/> 
    /// when the caller does not track the match.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenCallerDoesNotTrackMatch()
    {
        // Arrange
        var command = new AddUserToTrackedMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "caller-123", "target@test.com");

        _matchRepositoryMock
            .Setup(r => r.IsMatchCatchedByUserAsync(command.MatchId, command.TeamId, command.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("You must be tracking this match before sharing it with another user.");

        _userRepositoryMock.Verify(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that <see cref="AddUserToTrackedMatchHandler"/> throws <see cref="NotFoundException"/> 
    /// when target user with the specified email address is not found.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenTargetUserNotFound()
    {
        // Arrange
        var command = new AddUserToTrackedMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "caller-123", "nonexistent@test.com");

        _matchRepositoryMock
            .Setup(r => r.IsMatchCatchedByUserAsync(command.MatchId, command.TeamId, command.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<User>());

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"User with email '{command.Email}' was not found.");

        _matchRepositoryMock.Verify(r => r.CatchMatchAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that <see cref="AddUserToTrackedMatchHandler"/> links target user 
    /// and returns true when caller tracks match and target user exists.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnTrue_WhenSharingSucceeds()
    {
        // Arrange
        var command = new AddUserToTrackedMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "caller-123", "target@test.com");
        var targetUser = new User { Id = "target-456", Email = command.Email, DisplayName = "Target User" };

        _matchRepositoryMock
            .Setup(r => r.IsMatchCatchedByUserAsync(command.MatchId, command.TeamId, command.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { targetUser });

        _matchRepositoryMock
            .Setup(r => r.CatchMatchAsync(command.MatchId, command.TeamId, targetUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        _matchRepositoryMock.Verify(r => r.CatchMatchAsync(
            command.MatchId, command.TeamId, targetUser.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="AddUserToTrackedMatchHandler"/> throws <see cref="ConflictException"/> 
    /// when PostgreSQL raises business rule violation P0001 during match catching.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenPostgresExceptionP0001IsRaised()
    {
        // Arrange
        var command = new AddUserToTrackedMatchCommand(Guid.NewGuid(), Guid.NewGuid(), "caller-123", "target@test.com");
        var targetUser = new User { Id = "target-456", Email = command.Email, DisplayName = "Target User" };

        _matchRepositoryMock
            .Setup(r => r.IsMatchCatchedByUserAsync(command.MatchId, command.TeamId, command.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { targetUser });

        var pgException = new PostgresException("Team does not participate in match.", "Severity", "InvariantSeverity", "P0001");

        _matchRepositoryMock
            .Setup(r => r.CatchMatchAsync(command.MatchId, command.TeamId, targetUser.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Team does not participate in match.");
    }
}