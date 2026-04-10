using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Auth;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

/// <summary>
/// Unit tests for <see cref="TerminateMembershipHandler"/>.
/// Verifies membership resolution, transaction integrity, and exception handling.
/// </summary>
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

    /// <summary>
    /// Verifies that when a valid membership and access policy exist, 
    /// both are updated with the correct termination date and the handler returns true.
    /// </summary>
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

    /// <summary>
    /// Verifies that if no active membership is found, the handler throws a <see cref="NotFoundException"/>.
    /// This aligns with the domain logic requested by CodeRabbit.
    /// </summary>
    [Fact]
    public async Task Handle_MembershipNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "missing@test.com", TeamRole.HeadCoach, null);

        _membershipRepoMock
            .Setup(x => x.GetActiveMembershipByEmailAndRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TeamRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamMembership?)null);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*Active membership for {command.UserEmail}*not found*");

        VerifyLog(LogLevel.Warning, "Termination failed");
    }

    /// <summary>
    /// Verifies that the handler completes successfully even if there is no associated access policy to remove.
    /// Only the membership record should be updated in this case.
    /// </summary>
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
        _membershipRepoMock.Verify(x => x.TerminateMembershipAsync(membership, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that any unhandled exception from the repository layer bubbles up to the global handler.
    /// This ensures SonarCloud S2139 is satisfied by avoiding "log and throw" in the handler.
    /// </summary>
    [Fact]
    public async Task Handle_RepositoryThrows_ShouldBubbleUpException()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "error@test.com", TeamRole.Player, null);

        _membershipRepoMock
            .Setup(x => x.GetActiveMembershipByEmailAndRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TeamRole>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failure"));

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Database connection failure");
    }

    /// <summary>
    /// Helper method to verify logger calls.
    /// </summary>
    /// <param name="level">Expected LogLevel.</param>
    /// <param name="messageContains">Substring expected in the log message.</param>
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