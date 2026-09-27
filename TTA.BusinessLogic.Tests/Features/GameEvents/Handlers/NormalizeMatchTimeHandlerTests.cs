using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using System.Reflection;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Handlers;

/// <summary>
/// Unit tests for the <see cref="NormalizeMatchTimeHandler"/> class.
/// Validates business pipeline delegation, structured logging interaction, and database exception transformation rules.
/// </summary>
public class NormalizeMatchTimeHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<ILogger<NormalizeMatchTimeHandler>> _loggerMock;
    private readonly NormalizeMatchTimeHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="NormalizeMatchTimeHandlerTests"/> class.
    /// </summary>
    public NormalizeMatchTimeHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _loggerMock = new Mock<ILogger<NormalizeMatchTimeHandler>>();

        _handler = new NormalizeMatchTimeHandler(
            _gameEventRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully triggers the repository normalization sequence 
    /// and performs structured tracking logs when valid execution parameters are provided.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldDelegateToRepository_WhenCommandIsValid()
    {
        // Arrange
        var command = CreateCommand();

        _gameEventRepositoryMock
            .Setup(r => r.NormalizeMatchEventsTimeAsync(command.MatchId, command.TeamId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _gameEventRepositoryMock.Verify(
            r => r.NormalizeMatchEventsTimeAsync(command.MatchId, command.TeamId, It.IsAny<CancellationToken>()),
            Times.Once,
            "Handler must forward execution parameters directly to the underlying repository routine exactly once.");

        // Verify start and success log presence markers
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting batch match time normalization")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Batch match time normalization successfully completed")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a database-originated business rule exception (PostgresException state P0001) 
    /// is caught, logged with warning metrics, and correctly rethrown as a domain-level <see cref="ConflictException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenPostgresExceptionP0001IsRaised()
    {
        // Arrange
        var command = CreateCommand();
        var pgException = CreatePostgresException("P0001", "Match configuration invalid or not concluded.");

        _gameEventRepositoryMock
            .Setup(r => r.NormalizeMatchEventsTimeAsync(command.MatchId, command.TeamId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage(pgException.MessageText)
            .WithInnerException<ConflictException, PostgresException>(); // Corrected generic chaining for async FluentAssertions

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Batch match time normalization failed due to database business rule")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #region Helper Methods

    /// <summary>
    /// Creates a populated <see cref="NormalizeMatchTimeCommand"/> instance for isolation test scopes.
    /// </summary>
    /// <returns>A concrete command payload.</returns>
    private static NormalizeMatchTimeCommand CreateCommand()
    {
        return new NormalizeMatchTimeCommand(
            MatchId: Guid.NewGuid(),
            TeamId: Guid.NewGuid());
    }

    /// <summary>
    /// Reflectively constructs a concrete <see cref="PostgresException"/> for database exception mocking.
    /// </summary>
    /// <param name="sqlState">The 5-character custom PostgreSQL state indicator code.</param>
    /// <param name="messageText">The detail explanation text message.</param>
    /// <returns>An initialized instance of PostgresException.</returns>
    private static PostgresException CreatePostgresException(string sqlState, string messageText)
    {
        // Using reflective pattern mapping structure mirroring the project standards
        var constructor = typeof(PostgresException).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(string), typeof(string), typeof(string), typeof(string)],
            null);

        if (constructor != null)
        {
            return (PostgresException)constructor.Invoke([messageText, "ERROR", "ERROR", sqlState]);
        }

        return new PostgresException(messageText, "ERROR", "ERROR", sqlState);
    }

    #endregion
}