using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.BusinessLogic.Features.Teams.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetTeamByIdHandler"/>.
/// </summary>
public class GetTeamByIdHandlerTests
{
    private readonly Mock<ITeamRepository> _teamRepoMock;
    private readonly Mock<ILogger<GetTeamByIdHandler>> _loggerMock;
    private readonly GetTeamByIdHandler _handler;

    public GetTeamByIdHandlerTests()
    {
        _teamRepoMock = new Mock<ITeamRepository>();
        _loggerMock = new Mock<ILogger<GetTeamByIdHandler>>();

        _handler = new GetTeamByIdHandler(
            _teamRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when no team is found for the given team ID.
    /// </summary>
    [Fact]
    public async Task Handle_TeamDoesNotExist_ShouldThrowNotFoundException()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var query = new GetTeamByIdQuery(teamId);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Team?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.Handle(query, CancellationToken.None));

        Assert.Contains(teamId.ToString(), exception.Message);

        _teamRepoMock.Verify(x => x.GetByIdAsync(teamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler correctly maps a team entity into a <see cref="TeamResponse"/> DTO when the team exists.
    /// </summary>
    [Fact]
    public async Task Handle_TeamExists_ShouldReturnMappedTeamResponse()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var query = new GetTeamByIdQuery(teamId);

        var team = new Team
        {
            Id = teamId,
            ClubId = clubId,
            SportId = sportId,
            Name = "Water Polo Team",
            MinBirthYear = 2010,
            Gender = 0, // Male
            CreatedAt = DateTime.UtcNow
        };

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(team.Id, result.Id);
        Assert.Equal(team.ClubId, result.ClubId);
        Assert.Equal(team.SportId, result.SportId);
        Assert.Equal("Water Polo Team", result.Name);
        Assert.Equal(2010, result.MinBirthYear);
        Assert.Equal((int)team.Gender, result.Gender);
        Assert.Equal(team.CreatedAt, result.CreatedAt);

        _teamRepoMock.Verify(x => x.GetByIdAsync(teamId, It.IsAny<CancellationToken>()), Times.Once);
    }
}