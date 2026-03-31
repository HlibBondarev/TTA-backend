using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Players.Commands;
using TTA.BusinessLogic.Features.Players.Handlers;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Players.Handlers;

/// <summary>
/// Unit tests for the <see cref="CreatePlayerHandler"/>.
/// </summary>
public class CreatePlayerHandlerTests
{
    private readonly Mock<IPlayerRepository> _repositoryMock;
    private readonly Mock<ILogger<CreatePlayerHandler>> _loggerMock;
    private readonly CreatePlayerHandler _handler;

    public CreatePlayerHandlerTests()
    {
        _repositoryMock = new Mock<IPlayerRepository>();
        _loggerMock = new Mock<ILogger<CreatePlayerHandler>>();
        _handler = new CreatePlayerHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler correctly processes a valid command, 
    /// persists the player via repository, and returns the expected ID.
    /// </summary>
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreatePlayerAndReturnId()
    {
        // Arrange
        var command = new CreatePlayerCommand(
            HomeClubId: Guid.NewGuid(),
            FirstName: "Serhii",
            LastName: "Rebrov",
            BirthDate: new DateOnly(1974, 6, 3),
            Gender: Gender.Male
        );

        _repositoryMock
            .Setup(x => x.CreatePlayerAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player p, CancellationToken _) => p); // Return the same player object to get the ID

        // Act
        var resultId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, resultId);

        _repositoryMock.Verify(x => x.CreatePlayerAsync(
            It.Is<Player>(p =>
                p.FirstName == command.FirstName &&
                p.LastName == command.LastName &&
                p.HomeClubId == command.HomeClubId &&
                p.Gender == command.Gender &&
                p.BirthDate == command.BirthDate),
            It.IsAny<CancellationToken>()),
        Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Attempting to create player")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Ensures that the handler propagates exceptions if the repository fails.
    /// </summary>
    [Fact]
    public async Task Handle_RepositoryThrows_ShouldPropagateException()
    {
        // Arrange
        var command = new CreatePlayerCommand(
            Guid.NewGuid(), "Fail", "Test", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)), Gender.Female);

        _repositoryMock
            .Setup(x => x.CreatePlayerAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _handler.Handle(command, CancellationToken.None));
    }
}