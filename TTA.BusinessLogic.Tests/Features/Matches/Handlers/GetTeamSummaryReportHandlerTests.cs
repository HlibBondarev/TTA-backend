using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetTeamSummaryReportHandler"/> class.
/// Ensures correct mapping of repository summary projections to DTO responses.
/// </summary>
public class GetTeamSummaryReportHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<GetTeamSummaryReportHandler>> _loggerMock;
    private readonly GetTeamSummaryReportHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTeamSummaryReportHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public GetTeamSummaryReportHandlerTests()
    {
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<GetTeamSummaryReportHandler>>();

        _handler = new GetTeamSummaryReportHandler(
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a collection of populated summary DTOs
    /// when player records exist for the given team in a match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnSummaryResponses_When_ProjectionsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var query = new GetTeamSummaryReportQuery(matchId, teamId);

        var projections = new List<TeamMatchSummaryReportProjection>
        {
            new(
                MatchLineupId: Guid.NewGuid(),
                FirstName: "John",
                LastName: "Doe",
                Number: 10,
                Goals: 2,
                PositiveGoalLeadingActions: 1,
                NegativeGoalLeadingActions: 0,
                TotalPositiveActions: 5,
                TotalNegativeActions: 1,
                PlayPercentage: 85.5
            ),
            new(
                MatchLineupId: Guid.NewGuid(),
                FirstName: "Jane",
                LastName: "Smith",
                Number: 7,
                Goals: 0,
                PositiveGoalLeadingActions: 2,
                NegativeGoalLeadingActions: 1,
                TotalPositiveActions: 3,
                TotalNegativeActions: 2,
                PlayPercentage: 60.0
            )
        };

        _matchRepositoryMock
            .Setup(r => r.GetTeamSummaryReportAsync(matchId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultList = result.ToList();
        resultList.Should().HaveCount(2);

        resultList[0].FirstName.Should().Be("John");
        resultList[0].Goals.Should().Be(2);
        resultList[0].PlayPercentage.Should().Be(85.5);

        resultList[1].FirstName.Should().Be("Jane");
        resultList[1].PositiveGoalLeadingActions.Should().Be(2);
        resultList[1].PlayPercentage.Should().Be(60.0);

        _matchRepositoryMock.Verify(r => r.GetTeamSummaryReportAsync(matchId, teamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when no player summary records are found.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ThrowNotFoundException_When_NoProjectionsExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var query = new GetTeamSummaryReportQuery(matchId, teamId);

        _matchRepositoryMock
            .Setup(r => r.GetTeamSummaryReportAsync(matchId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        Func<Task> act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Team summary report for team {teamId} in match {matchId} not found.");

        _matchRepositoryMock.Verify(r => r.GetTeamSummaryReportAsync(matchId, teamId, It.IsAny<CancellationToken>()), Times.Once);
    }
}