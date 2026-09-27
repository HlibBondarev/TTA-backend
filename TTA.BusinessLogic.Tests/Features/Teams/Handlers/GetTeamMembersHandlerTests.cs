using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.BusinessLogic.Features.Teams.Queries;
using TTA.Common.Exceptions;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetTeamMembersHandler"/>.
/// </summary>
public class GetTeamMembersHandlerTests
{
    private readonly Mock<ITeamMembershipRepository> _membershipRepoMock;
    private readonly Mock<ITeamRepository> _teamRepoMock;
    private readonly Mock<ILogger<GetTeamMembersHandler>> _loggerMock;
    private readonly GetTeamMembersHandler _handler;

    public GetTeamMembersHandlerTests()
    {
        _membershipRepoMock = new Mock<ITeamMembershipRepository>();
        _teamRepoMock = new Mock<ITeamRepository>();
        _loggerMock = new Mock<ILogger<GetTeamMembersHandler>>();

        _handler = new GetTeamMembersHandler(
            _membershipRepoMock.Object,
            _teamRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when the requested team does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_TeamDoesNotExist_ShouldThrowNotFoundException()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var query = new GetTeamMembersQuery(teamId);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Team?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.Handle(query, CancellationToken.None));

        Assert.Contains(teamId.ToString(), exception.Message);

        _membershipRepoMock.Verify(x => x.GetMembersJsonAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that an empty collection is returned when the team exists but has no active members.
    /// </summary>
    [Fact]
    public async Task Handle_TeamExistsButNoMembers_ShouldReturnEmptyList()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var query = new GetTeamMembersQuery(teamId);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = teamId });

        _membershipRepoMock
            .Setup(x => x.GetMembersJsonAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Empty(result);
        _membershipRepoMock.Verify(x => x.GetMembersJsonAsync(teamId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the handler correctly deserializes the JSON string from the repository 
    /// into a collection of <see cref="TeamMemberResponse"/>.
    /// </summary>
    [Fact]
    public async Task Handle_TeamHasMembers_ShouldReturnPopulatedList()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var query = new GetTeamMembersQuery(teamId);

        var members = new List<TeamMemberResponse>
        {
            new (Guid.NewGuid(), "auth0|1", "Player One", "player1@test.com", 2, DateTime.UtcNow, false),
            new (Guid.NewGuid(), "auth0|2", "Coach One", "coach1@test.com", 0, DateTime.UtcNow, true)
        };

        // Use the same options as the handler to ensure compatibility
        var jsonResponse = JsonSerializer.Serialize(members, new JsonSerializerOptions().GetDefault());

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = teamId });

        _membershipRepoMock
            .Setup(x => x.GetMembersJsonAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        var resultList = result.ToList();
        Assert.Equal(2, resultList.Count);
        Assert.Equal("Player One", resultList[0].DisplayName);
        Assert.Equal("Coach One", resultList[1].DisplayName);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Team {teamId}")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}