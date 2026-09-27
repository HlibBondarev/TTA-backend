using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

/// <summary>
/// Unit tests for the <see cref="CreateTeamHandler"/>.
/// </summary>
public class CreateTeamHandlerTests
{
    private readonly Mock<ITeamRepository> _repositoryMock;
    private readonly Mock<ILogger<CreateTeamHandler>> _loggerMock;
    private readonly CreateTeamHandler _handler;

    public CreateTeamHandlerTests()
    {
        _repositoryMock = new Mock<ITeamRepository>();
        _loggerMock = new Mock<ILogger<CreateTeamHandler>>();
        _handler = new CreateTeamHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler correctly processes a valid command, 
    /// persists the team via repository, and returns the expected ID.
    /// </summary>
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateTeamAndReturnId()
    {
        // Arrange
        var command = new CreateTeamCommand(
            ClubId: Guid.NewGuid(),
            Name: "U-17 Academy",
            SportId: Guid.NewGuid(),
            MinBirthYear: 2007,
            Gender: Gender.Male
        );

        _repositoryMock
            .Setup(x => x.CreateTeamAsync(It.IsAny<Team>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Team t, CancellationToken _) => t); // Return the same team object to get the ID

        // Act
        var resultId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, resultId);

        _repositoryMock.Verify(x => x.CreateTeamAsync(
            It.Is<Team>(t =>
                t.Name == command.Name &&
                t.ClubId == command.ClubId &&
                t.SportId == command.SportId &&
                t.Gender == command.Gender &&
                t.MinBirthYear == command.MinBirthYear),
            It.IsAny<CancellationToken>()),
        Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Creating new team")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Ensures that the handler propagates exceptions if the repository fails.
    /// </summary>
    [Fact]
    public async Task Handle_RepositoryThrows_ShouldPropagateException()
    {
        // Arrange
        var command = new CreateTeamCommand(
            Guid.NewGuid(), "Failure Team", Guid.NewGuid(), 2010, Gender.Female);

        _repositoryMock
            .Setup(x => x.CreateTeamAsync(It.IsAny<Team>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _handler.Handle(command, CancellationToken.None));
    }
}