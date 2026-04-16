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
/// Unit tests for the <see cref="GetTeamRosterHandler"/> ensuring correct tournament and team validation
/// and accurate mapping of dynamic repository results to structured DTOs.
/// </summary>
public class GetTeamRosterHandlerTests
{
    private readonly Mock<IRosterRepository> _rosterRepoMock;
    private readonly Mock<ITeamRepository> _teamRepoMock;
    private readonly Mock<ITournamentRepository> _tournamentRepoMock;
    private readonly Mock<ILogger<GetTeamRosterHandler>> _loggerMock;
    private readonly GetTeamRosterHandler _handler;

    public GetTeamRosterHandlerTests()
    {
        _rosterRepoMock = new Mock<IRosterRepository>();
        _teamRepoMock = new Mock<ITeamRepository>();
        _tournamentRepoMock = new Mock<ITournamentRepository>();
        _loggerMock = new Mock<ILogger<GetTeamRosterHandler>>();

        _handler = new GetTeamRosterHandler(
            _rosterRepoMock.Object,
            _teamRepoMock.Object,
            _tournamentRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully returns a mapped collection of roster players
    /// when both the tournament and the team exist and contain valid data.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnMappedRoster()
    {
        // Arrange
        var query = new GetTeamRosterQuery(Guid.NewGuid(), Guid.NewGuid());

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(query.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = query.TournamentId });

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = query.TeamId, Name = "Test Team" });

        dynamic row1 = new ExpandoObject();
        row1.id = Guid.NewGuid();
        row1.playerid = Guid.NewGuid();
        row1.firstname = "Andriy";
        row1.lastname = "Shevchenko";
        row1.positionid = Guid.NewGuid();
        row1.positionname = "Forward";
        row1.number = 7;

        var rawData = new List<object> { row1 };

        _rosterRepoMock
            .Setup(x => x.GetTeamRosterAsync(query.TournamentId, query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawData);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].FirstName.Should().Be("Andriy");
        result[0].Number.Should().Be(7);
    }

    /// <summary>
    /// Ensures that a <see cref="NotFoundException"/> is thrown immediately if the specified 
    /// tournament does not exist, preventing further team or roster lookups.
    /// </summary>
    [Fact]
    public async Task Handle_TournamentNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var query = new GetTeamRosterQuery(Guid.NewGuid(), Guid.NewGuid());

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(query.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament)null!);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*Tournament with ID {query.TournamentId}*");

        _teamRepoMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _rosterRepoMock.Verify(x => x.GetTeamRosterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Ensures that a <see cref="NotFoundException"/> is thrown if the tournament exists 
    /// but the specified team is not found in the database.
    /// </summary>
    [Fact]
    public async Task Handle_TeamNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var query = new GetTeamRosterQuery(Guid.NewGuid(), Guid.NewGuid());

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(query.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = query.TournamentId });

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(query.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Team)null!);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*Team with ID {query.TeamId}*");

        _rosterRepoMock.Verify(x => x.GetTeamRosterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler returns an empty collection instead of throwing an error 
    /// when the tournament and team are valid but no players are currently registered in the roster.
    /// </summary>
    [Fact]
    public async Task Handle_EmptyRoster_ShouldReturnEmptyCollection()
    {
        // Arrange
        var query = new GetTeamRosterQuery(Guid.NewGuid(), Guid.NewGuid());

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(query.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = query.TournamentId });

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