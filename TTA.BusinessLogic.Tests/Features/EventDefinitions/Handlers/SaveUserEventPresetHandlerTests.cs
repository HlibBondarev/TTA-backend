using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.BusinessLogic.Features.EventDefinitions.Handlers;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Handlers;

/// <summary>
/// Unit tests for <see cref="SaveUserEventPresetHandler"/>.
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
}