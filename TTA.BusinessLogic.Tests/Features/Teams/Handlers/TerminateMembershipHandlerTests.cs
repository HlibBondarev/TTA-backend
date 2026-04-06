using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

/// <summary>
/// Unit tests for the <see cref="TerminateMembershipHandler"/>.
/// </summary>
public class TerminateMembershipHandlerTests
{
    private readonly Mock<ITeamMembershipRepository> _membershipRepoMock;
    private readonly Mock<ILogger<TerminateMembershipHandler>> _loggerMock;
    private readonly TerminateMembershipHandler _handler;

    public TerminateMembershipHandlerTests()
    {
        _membershipRepoMock = new Mock<ITeamMembershipRepository>();
        _loggerMock = new Mock<ILogger<TerminateMembershipHandler>>();
        _handler = new TerminateMembershipHandler(_membershipRepoMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns <c>true</c> and logs both the attempt and the success messages 
    /// when the repository successfully terminates a membership.
    /// </summary>
    [Fact]
    public async Task Handle_MembershipExists_ShouldReturnTrueAndLogInformation()
    {
        // Arrange
        var membershipId = Guid.NewGuid();
        var command = new TerminateMembershipCommand(membershipId);

        _membershipRepoMock
            .Setup(x => x.TerminateMembershipAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result);

        _membershipRepoMock.Verify(x => x.TerminateMembershipAsync(membershipId, It.IsAny<CancellationToken>()),
            Times.Once);

        // 1. Verify the "Attempting" log (First log in the handler)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Attempting to terminate")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // 2. Verify the "Success" log (Second log in the handler)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("successfully terminated")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns <c>false</c> and logs a warning message 
    /// when the membership ID is not found or is already terminated.
    /// </summary>
    [Fact]
    public async Task Handle_MembershipNotFound_ShouldReturnFalseAndLogWarning()
    {
        // Arrange
        var membershipId = Guid.NewGuid();
        var command = new TerminateMembershipCommand(membershipId);

        _membershipRepoMock
            .Setup(x => x.TerminateMembershipAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("was not found or already terminated")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}