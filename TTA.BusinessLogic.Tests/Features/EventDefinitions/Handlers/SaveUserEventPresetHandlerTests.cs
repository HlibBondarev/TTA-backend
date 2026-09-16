using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.BusinessLogic.Features.EventDefinitions.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Handlers;

/// <summary>
/// Unit tests for < see cref="SaveUserEventPresetHandler" />.
/// </summary>
public class SaveUserEventPresetHandlerTests
{
    private readonly Mock<IUserEventPresetRepository> _repositoryMock;
    private readonly Mock<ILogger<SaveUserEventPresetHandler>> _loggerMock;
    private readonly SaveUserEventPresetHandler _handler;

    public SaveUserEventPresetHandlerTests()
    {
        _repositoryMock = new Mock<IUserEventPresetRepository>();
        _loggerMock = new Mock<ILogger<SaveUserEventPresetHandler>>();
        _handler = new SaveUserEventPresetHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="SaveUserEventPresetHandler.Handle"/> delegates the user preset persistence
    /// directly to the repository layer with matching parameters.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldCallSavePresetAsync_WhenCommandIsValid()
    {
        // Arrange
        var userId = "auth0|user123";
        var sportId = Guid.NewGuid();
        var eventDefIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var command = new SaveUserEventPresetCommand(userId, sportId, eventDefIds);

        _repositoryMock
            .Setup(r => r.SavePresetAsync(userId, sportId, eventDefIds, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(r => r.SavePresetAsync(userId, sportId, eventDefIds, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="SaveUserEventPresetHandler.Handle"/> passes 
    /// the provided cancellation token directly to the repository layer call.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPassCancellationTokenToRepository()
    {
        // Arrange
        var userId = "auth0|user123";
        var sportId = Guid.NewGuid();
        var eventDefIds = new List<Guid> { Guid.NewGuid() };

        var command = new SaveUserEventPresetCommand(userId, sportId, eventDefIds);

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _repositoryMock
            .Setup(r => r.SavePresetAsync(userId, sportId, eventDefIds, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(command, cancellationToken);

        // Assert
        _repositoryMock.Verify(r => r.SavePresetAsync(userId, sportId, eventDefIds, cancellationToken), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="SaveUserEventPresetHandler.Handle"/> catches PostgresException with P0001
    /// and throws a ConflictException.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenPostgresExceptionP0001IsRaised()
    {
        // Arrange
        var userId = "auth0|user123";
        var sportId = Guid.NewGuid();
        var eventDefIds = new List<Guid> { Guid.NewGuid() };
        var command = new SaveUserEventPresetCommand(userId, sportId, eventDefIds);

        var postgresException = new PostgresException("Invalid event definition ID", "ERROR", "ERROR", "P0001");

        _repositoryMock
            .Setup(r => r.SavePresetAsync(userId, sportId, eventDefIds, It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Invalid event definition ID");
    }
}