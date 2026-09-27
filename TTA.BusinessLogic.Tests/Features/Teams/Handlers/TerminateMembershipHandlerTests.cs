using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Data;
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
    private readonly Mock<IDbConnection> _connectionMock;
    private readonly Mock<IDbTransaction> _transactionMock;
    private readonly TerminateMembershipHandler _handler;

    public TerminateMembershipHandlerTests()
    {
        _membershipRepoMock = new Mock<ITeamMembershipRepository>();
        _accessRepoMock = new Mock<IAccessRepository>();
        _loggerMock = new Mock<ILogger<TerminateMembershipHandler>>();

        // Infrastructure mocks for transaction coordination
        _connectionMock = new Mock<IDbConnection>();
        _transactionMock = new Mock<IDbTransaction>();

        // Setup the connection to return a mock transaction
        _connectionMock.Setup(x => x.BeginTransaction()).Returns(_transactionMock.Object);

        // Setup repository to return the mock connection
        _membershipRepoMock
            .Setup(x => x.OpenConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_connectionMock.Object);

        _handler = new TerminateMembershipHandler(
            _membershipRepoMock.Object,
            _accessRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that when a valid membership and access policy exist, 
    /// both are updated within the same transaction and the handler returns true.
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

        // Verify that repositories use the shared connection and transaction
        _membershipRepoMock.Verify(x => x.TerminateMembershipAsync(
            membership, _connectionMock.Object, _transactionMock.Object, It.IsAny<CancellationToken>()), Times.Once);

        _accessRepoMock.Verify(x => x.RemoveAccessAsync(
            accessPolicy, _connectionMock.Object, _transactionMock.Object, It.IsAny<CancellationToken>()), Times.Once);

        _transactionMock.Verify(x => x.Commit(), Times.Once);
    }

    /// <summary>
    /// Verifies that if no active membership is found, the handler throws a <see cref="NotFoundException"/>
    /// and does not attempt to open a transaction.
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

        _membershipRepoMock.Verify(x => x.OpenConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyLog(LogLevel.Warning, "Termination failed");
    }

    /// <summary>
    /// Verifies that the handler completes successfully even if there is no associated access policy to remove.
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
        _accessRepoMock.Verify(x => x.RemoveAccessAsync(
            It.IsAny<AccessPolicy>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()), Times.Never);

        _membershipRepoMock.Verify(x => x.TerminateMembershipAsync(
            membership, _connectionMock.Object, _transactionMock.Object, It.IsAny<CancellationToken>()), Times.Once);

        _transactionMock.Verify(x => x.Commit(), Times.Once);
    }

    /// <summary>
    /// Verifies that any repository exception bubbles up and transaction is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_RepositoryThrows_ShouldBubbleUpException()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "error@test.com", TeamRole.Player, null);
        var membership = new TeamMembership { UserId = "user-123", TeamId = command.TeamId };

        _membershipRepoMock
            .Setup(x => x.GetActiveMembershipByEmailAndRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TeamRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        _membershipRepoMock
            .Setup(x => x.TerminateMembershipAsync(It.IsAny<TeamMembership>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failure"));

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Database connection failure");

        _transactionMock.Verify(x => x.Commit(), Times.Never);
    }

    /// <summary>
    /// Verifies the atomicity of the membership termination process by simulating a partial failure.
    /// Specifically, it ensures that if the access revocation step fails after a successful membership update, 
    /// the exception is propagated to trigger a database rollback.
    /// </summary>
    /// <remarks>
    /// This test validates that the handler respects the shared transaction lifecycle:
    /// 1. Membership is marked as terminated in the database.
    /// 2. An exception is thrown during access policy expiration.
    /// 3. The handler allows the exception to bubble up, preventing the transaction from committing.
    /// </remarks>
    [Fact]
    public async Task Handle_ShouldThrowAndRollback_WhenAccessRevocationFails()
    {
        // Arrange
        var command = new TerminateMembershipCommand(
            Guid.NewGuid(),
            "member@example.com",
            TeamRole.Player,
            DateTime.UtcNow);

        var membership = new TeamMembership { Id = Guid.NewGuid(), UserId = "user-1" };
        var policy = new AccessPolicy { Id = Guid.NewGuid(), UserId = "user-1" };
        var expectedExceptionMessage = "Database connection lost during access revocation";

        _membershipRepoMock
            .Setup(x => x.GetActiveMembershipByEmailAndRoleAsync(command.TeamId, command.UserEmail, command.RoleInTeam, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        _membershipRepoMock
            .Setup(x => x.TerminateMembershipAsync(It.IsAny<TeamMembership>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _accessRepoMock
            .Setup(x => x.GetActiveTeamPolicyAsync(membership.UserId, command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Mock RemoveAccessAsync to throw an exception
        _accessRepoMock
            .Setup(x => x.RemoveAccessAsync(It.IsAny<AccessPolicy>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(expectedExceptionMessage));

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage(expectedExceptionMessage);

        // Verify that termination was attempted but the failure in access revocation stopped the flow
        _membershipRepoMock.Verify(x => x.TerminateMembershipAsync(
            It.IsAny<TeamMembership>(),
            It.IsAny<IDbConnection>(),
            It.IsAny<IDbTransaction>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _accessRepoMock.Verify(x => x.RemoveAccessAsync(
            It.IsAny<AccessPolicy>(),
            It.IsAny<IDbConnection>(),
            It.IsAny<IDbTransaction>(),
            It.IsAny<CancellationToken>()), Times.Once);
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