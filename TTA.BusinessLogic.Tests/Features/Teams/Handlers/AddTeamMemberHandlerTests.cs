using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

/// <summary>
/// Unit tests for the <see cref="AddTeamMemberHandler"/>.
/// </summary>
public class AddTeamMemberHandlerTests
{
    private readonly Mock<ITeamMembershipRepository> _membershipRepoMock;
    private readonly Mock<ITeamRepository> _teamRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<ILogger<AddTeamMemberHandler>> _loggerMock;
    private readonly AddTeamMemberHandler _handler;

    public AddTeamMemberHandlerTests()
    {
        _membershipRepoMock = new Mock<ITeamMembershipRepository>();
        _teamRepoMock = new Mock<ITeamRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<AddTeamMemberHandler>>();

        _handler = new AddTeamMemberHandler(
            _membershipRepoMock.Object,
            _teamRepoMock.Object,
            _userRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the target team does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_TeamNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new AddTeamMemberCommand(Guid.NewGuid(), "auth0|user123", TeamRole.AssistantCoach, true);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Team?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.Handle(command, CancellationToken.None));

        _membershipRepoMock.Verify(x => x.CreateMembershipAsync(It.IsAny<TeamMembership>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the user to be added does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_UserNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new AddTeamMemberCommand(Guid.NewGuid(), "non-existent-user", TeamRole.HeadCoach, true);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        _userRepoMock
            .Setup(x => x.GetByIdAsync(command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.Handle(command, CancellationToken.None));
    }

    /// <summary>
    /// Verifies that a valid command results in a new membership being persisted with correct properties.
    /// </summary>
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateMembershipAndReturnId()
    {
        // Arrange
        var command = new AddTeamMemberCommand(
            TeamId: Guid.NewGuid(),
            UserId: "auth0|valid-user",
            RoleInTeam: TeamRole.AssistantCoach,
            IsPrimary: false
        );

        var expectedId = Guid.NewGuid();

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        _userRepoMock
            .Setup(x => x.GetByIdAsync(command.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = command.UserId });

        _membershipRepoMock
            .Setup(x => x.CreateMembershipAsync(It.IsAny<TeamMembership>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamMembership m, CancellationToken _) =>
            {
                m.Id = expectedId; // Simulate DB assigning an ID (or the ToModel assignment)
                return m;
            });

        // Act
        var resultId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(expectedId, resultId);

        _membershipRepoMock.Verify(x => x.CreateMembershipAsync(
            It.Is<TeamMembership>(m =>
                m.TeamId == command.TeamId &&
                m.UserId == command.UserId &&
                m.RoleInTeam == command.RoleInTeam &&
                m.IsPrimary == command.IsPrimary),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Matches the string: "Processing AddTeamMemberCommand for User..."
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing AddTeamMemberCommand")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}