using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.PlayerPresences.Handlers;
using TTA.BusinessLogic.Features.PlayerPresences.Notifications;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Handlers;

/// <summary>
/// Unit tests for the <see cref="ClosePresencesOnPeriodEndHandler"/> class.
/// Ensures that the notification properly triggers the repository method to close active player sessions.
/// </summary>
public class ClosePresencesOnPeriodEndHandlerTests
{
    private readonly Mock<IPlayerPresenceRepository> _playerPresenceRepositoryMock;
    private readonly Mock<ILogger<ClosePresencesOnPeriodEndHandler>> _loggerMock;
    private readonly ClosePresencesOnPeriodEndHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClosePresencesOnPeriodEndHandlerTests"/> class.
    /// Sets up required repository and logger mocks.
    /// </summary>
    public ClosePresencesOnPeriodEndHandlerTests()
    {
        _playerPresenceRepositoryMock = new Mock<IPlayerPresenceRepository>();
        _loggerMock = new Mock<ILogger<ClosePresencesOnPeriodEndHandler>>();

        _handler = new ClosePresencesOnPeriodEndHandler(
            _playerPresenceRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler correctly extracts data from the notification 
    /// and passes it to the repository to close active presences.
    /// </summary>
    [Fact]
    public async Task Handle_Should_CallCloseActivePresences_WithCorrectParameters()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var periodNumber = 2;
        var endTime = DateTime.UtcNow;
        var notification = new PeriodEndedNotification(matchId, periodNumber, endTime);

        _playerPresenceRepositoryMock
            .Setup(r => r.CloseActivePresencesAsync(matchId, periodNumber, endTime, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _handler.Handle(notification, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        // Verify that the repository was called exactly once with the exact parameters from the notification
        _playerPresenceRepositoryMock.Verify(r => r.CloseActivePresencesAsync(
            notification.MatchId,
            notification.PeriodNumber,
            notification.EndTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that if the repository throws an exception during the closing process, 
    /// the handler propagates it upwards without swallowing it.
    /// </summary>
    [Fact]
    public async Task Handle_Should_PropagateException_When_RepositoryThrows()
    {
        // Arrange
        var notification = new PeriodEndedNotification(Guid.NewGuid(), 1, DateTime.UtcNow);
        var expectedException = new PostgresException("Database connection failed", "ERROR", "ERROR", "08000");

        _playerPresenceRepositoryMock
            .Setup(r => r.CloseActivePresencesAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        var act = async () => await _handler.Handle(notification, CancellationToken.None);

        // Assert
        var exceptionAssertion = await act.Should().ThrowAsync<PostgresException>();

        // Use wildcards (*) because PostgresException automatically prefixes its Message property with the SqlState (e.g., "08000: ...")
        exceptionAssertion.WithMessage("*Database connection failed*");

        _playerPresenceRepositoryMock.Verify(r => r.CloseActivePresencesAsync(
            notification.MatchId,
            notification.PeriodNumber,
            notification.EndTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}