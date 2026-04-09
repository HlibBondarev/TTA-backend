using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

public class TerminateMembershipHandlerTests
{
    private readonly Mock<ITeamMembershipRepository> _membershipRepoMock;
    private readonly Mock<IAccessRepository> _accessRepoMock;
    private readonly Mock<ILogger<TerminateMembershipHandler>> _loggerMock;
    private readonly TerminateMembershipHandler _handler;

    public TerminateMembershipHandlerTests()
    {
        _membershipRepoMock = new Mock<ITeamMembershipRepository>();
        _accessRepoMock = new Mock<IAccessRepository>();
        _loggerMock = new Mock<ILogger<TerminateMembershipHandler>>();

        _handler = new TerminateMembershipHandler(
            _membershipRepoMock.Object,
            _accessRepoMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_MembershipExists_ShouldReturnTrueAndTerminateBothEntities()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var email = "player@example.com";
        var terminationDate = DateTime.UtcNow;

        var command = new TerminateMembershipCommand(teamId, email, TeamRole.Player, terminationDate);

        var membership = new TeamMembership { UserId = "user-123", TeamId = teamId };
        var accessPolicy = new AccessPolicy { Id = Guid.NewGuid(), UserId = "user-123" };

        _membershipRepoMock
            .Setup(x => x.GetActiveMembershipByEmailAndRoleAsync(teamId, email, TeamRole.Player, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        _accessRepoMock
            .Setup(x => x.GetActiveTeamPolicyAsync(membership.UserId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessPolicy);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        membership.LeftAt.Should().Be(terminationDate);
        accessPolicy.ExpiresAt.Should().Be(terminationDate);

        _membershipRepoMock.Verify(x => x.TerminateMembershipAsync(membership, It.IsAny<CancellationToken>()), Times.Once);
        _accessRepoMock.Verify(x => x.RemoveAccessAsync(accessPolicy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MembershipNotFound_ShouldReturnFalse()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "missing@test.com", TeamRole.HeadCoach, null);

        _membershipRepoMock
            .Setup(x => x.GetActiveMembershipByEmailAndRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TeamRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamMembership?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        VerifyLog(LogLevel.Warning, "Termination failed");
    }

    [Fact]
    public async Task Handle_NoAccessPolicy_ShouldStillReturnTrue()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "no-policy@test.com", TeamRole.Player, null);
        var membership = new TeamMembership { UserId = "user-123", TeamId = command.TeamId };

        _membershipRepoMock
            .Setup(x => x.GetActiveMembershipByEmailAndRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TeamRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        _accessRepoMock
            .Setup(x => x.GetActiveTeamPolicyAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessPolicy?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _accessRepoMock.Verify(x => x.RemoveAccessAsync(It.IsAny<AccessPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyLog(LogLevel level, string messageContains)
    {
        _loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(messageContains)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

//using Microsoft.Extensions.Logging;
//using Moq;
//using TTA.BusinessLogic.Features.Teams.Handlers;
//using TTA.DataAccess.Repository.Api;

//namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

///// <summary>
///// Unit tests for the <see cref="TerminateMembershipHandler"/>, ensuring scoped termination logic.
///// </summary>
//public class TerminateMembershipHandlerTests
//{
//    private readonly Mock<ITeamMembershipRepository> _membershipRepoMock;
//    private readonly Mock<ILogger<TerminateMembershipHandler>> _loggerMock;
//    private readonly TerminateMembershipHandler _handler;

//    public TerminateMembershipHandlerTests()
//    {
//        _membershipRepoMock = new Mock<ITeamMembershipRepository>();
//        _loggerMock = new Mock<ILogger<TerminateMembershipHandler>>();
//        _handler = new TerminateMembershipHandler(_membershipRepoMock.Object, null, _loggerMock.Object);
//    }

//    /// <summary>
//    /// Verifies that the handler returns <c>true</c> and logs both the attempt and the success messages 
//    /// when the repository successfully terminates a membership scoped to a specific team.
//    /// </summary>
//    [Fact]
//    public async Task Handle_MembershipExists_ShouldReturnTrueAndLogInformation()
//    {
//        // Arrange
//        var teamId = Guid.NewGuid();
//        var membershipId = Guid.NewGuid();
//        var command = new TerminateMembershipCommand(teamId, membershipId);

//        _membershipRepoMock
//            .Setup(x => x.TerminateMembershipAsync(teamId, membershipId, It.IsAny<CancellationToken>()))
//            .ReturnsAsync(true);

//        // Act
//        var result = await _handler.Handle(command, CancellationToken.None);

//        // Assert
//        Assert.True(result);

//        // Verify repository call with both scoped IDs
//        _membershipRepoMock.Verify(x => x.TerminateMembershipAsync(teamId, membershipId, It.IsAny<CancellationToken>()),
//            Times.Once);

//        // Verify "Attempting" log includes mention of team or membership
//        _loggerMock.Verify(
//            x => x.Log(
//                LogLevel.Information,
//                It.IsAny<EventId>(),
//                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Attempting to terminate")),
//                null,
//                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
//            Times.Once);

//        // Verify "Success" log
//        _loggerMock.Verify(
//            x => x.Log(
//                LogLevel.Information,
//                It.IsAny<EventId>(),
//                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("successfully terminated")),
//                null,
//                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
//            Times.Once);
//    }

//    /// <summary>
//    /// Verifies that the handler returns <c>false</c> and logs a warning message 
//    /// when the membership ID is not found within the specified team context.
//    /// </summary>
//    [Fact]
//    public async Task Handle_MembershipNotFound_ShouldReturnFalseAndLogWarning()
//    {
//        // Arrange
//        var teamId = Guid.NewGuid();
//        var membershipId = Guid.NewGuid();
//        var command = new TerminateMembershipCommand(teamId, membershipId);

//        _membershipRepoMock
//            .Setup(x => x.TerminateMembershipAsync(teamId, membershipId, It.IsAny<CancellationToken>()))
//            .ReturnsAsync(false);

//        // Act
//        var result = await _handler.Handle(command, CancellationToken.None);

//        // Assert
//        Assert.False(result);

//        // Verify warning log message
//        _loggerMock.Verify(
//            x => x.Log(
//                LogLevel.Warning,
//                It.IsAny<EventId>(),
//                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("was not found or already terminated")),
//                null,
//                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
//            Times.Once);
//    }
//}