using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.Rosters.Handlers;
using TTA.BusinessLogic.Features.Rosters.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Rosters.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetTeamRosterHandler"/> ensuring correct team validation
/// and accurate mapping of dynamic repository results to structured DTOs.
/// </summary>
public class GetTeamRosterHandlerTests
{
    private readonly Mock<IRosterRepository> _rosterRepoMock;
    private readonly Mock<ITeamRepository> _teamRepoMock;
    private readonly Mock<ILogger<GetTeamRosterHandler>> _loggerMock;
    private readonly GetTeamRosterHandler _handler;

    public GetTeamRosterHandlerTests()
    {
        _rosterRepoMock = new Mock<IRosterRepository>();
        _teamRepoMock = new Mock<ITeamRepository>();
        _loggerMock = new Mock<ILogger<GetTeamRosterHandler>>();

        _handler = new GetTeamRosterHandler(
            _rosterRepoMock.Object,
            _teamRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a correctly mapped collection of <see cref="RosterPlayerResponse"/>
    /// when the team exists and has players.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnMappedRoster()
    {
        // Arrange
        var query = new GetTeamRosterQuery(Guid.NewGuid(), Guid.NewGuid());
        var team = new Team { Id = query.TeamId, Name = "Test Team" };

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        // Use ExpandoObject to simulate dynamic Dapper results across assembly boundaries
        dynamic row1 = new ExpandoObject();
        row1.id = Guid.NewGuid();
        row1.playerid = Guid.NewGuid();
        row1.firstname = "Andriy";
        row1.lastname = "Shevchenko";
        row1.positionid = Guid.NewGuid();
        row1.positionname = "Forward";
        row1.number = 7;

        dynamic row2 = new ExpandoObject();
        row2.id = Guid.NewGuid();
        row2.playerid = Guid.NewGuid();
        row2.firstname = "Oleksandr";
        row2.lastname = "Zinchenko";
        row2.positionid = Guid.NewGuid();
        row2.positionname = "Midfielder";
        row2.number = 11;

        var rawData = new List<object> { row1, row2 };

        _rosterRepoMock
            .Setup(x => x.GetTeamRosterAsync(query.TournamentId, query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawData);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(2);

        result[0].FirstName.Should().Be("Andriy");
        result[0].Number.Should().Be(7);
        result[0].PositionName.Should().Be("Forward");

        result[1].FirstName.Should().Be("Oleksandr");
        result[1].Number.Should().Be(11);
        result[1].PositionName.Should().Be("Midfielder");
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when the team is not found in the database.
    /// </summary>
    [Fact]
    public async Task Handle_TeamNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var query = new GetTeamRosterQuery(Guid.NewGuid(), Guid.NewGuid());

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Team)null!);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*ID {query.TeamId}*");

        _rosterRepoMock.Verify(x => x.GetTeamRosterAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that an empty collection is returned if the team exists but has no players assigned.
    /// </summary>
    [Fact]
    public async Task Handle_EmptyRoster_ShouldReturnEmptyCollection()
    {
        // Arrange
        var query = new GetTeamRosterQuery(Guid.NewGuid(), Guid.NewGuid());

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = query.TeamId });

        _rosterRepoMock
            .Setup(x => x.GetTeamRosterAsync(query.TournamentId, query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}