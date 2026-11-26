using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.BusinessLogic.Services.Api;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Controllers;

namespace TTA.WebAPI.Tests.Controllers;

/// <summary>
/// Unit tests for <see cref="MatchesController.GetMatchAnalysisSnapshot"/>.
/// </summary>
public class MatchesControllerAnalysisSnapshotTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<IValidator<GetMatchAnalysisSnapshotQuery>> _validatorMock = new();
    private readonly MatchesController _controller;

    public MatchesControllerAnalysisSnapshotTests()
    {
        _controller = new MatchesController(
            _mediatorMock.Object,
            Mock.Of<IAccessService>(),
            Mock.Of<ILogger<MatchesController>>(),
            new Auth0Settings("https://test.auth0.com/", "test-client", "test-api", "https://tta-api.com/"));
    }

    /// <summary>
    /// Verifies that a valid request dispatches the query with the cancellation token and returns HTTP 200 with the snapshot.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldReturnOk_WhenQueryIsValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var snapshot = BuildResponse();
        var query = new GetMatchAnalysisSnapshotQuery(matchId, teamId);

        _validatorMock
            .Setup(v => v.ValidateAsync(query, cts.Token))
            .ReturnsAsync(new ValidationResult());
        _mediatorMock
            .Setup(m => m.Send(query, cts.Token))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _controller.GetMatchAnalysisSnapshot(matchId, teamId, _validatorMock.Object, cts.Token);

        // Assert
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(snapshot);
        _mediatorMock.Verify(m => m.Send(query, cts.Token), Times.Once);
    }

    /// <summary>
    /// Verifies that an invalid request returns HTTP 400 and never reaches the mediator.
    /// </summary>
    [Fact]
    public async Task GetMatchAnalysisSnapshot_ShouldReturnBadRequest_WhenValidationFails()
    {
        // Arrange
        _validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<GetMatchAnalysisSnapshotQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("MatchId", "Match ID is required.")]));

        // Act
        var result = await _controller.GetMatchAnalysisSnapshot(Guid.Empty, Guid.NewGuid(), _validatorMock.Object, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetMatchAnalysisSnapshotQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static MatchAnalysisSnapshotResponse BuildResponse()
    {
        var team = new MatchAnalysisTeamResponse(Guid.NewGuid(), "Team");
        return new MatchAnalysisSnapshotResponse(
            new MatchAnalysisMatchResponse(Guid.NewGuid(), Guid.NewGuid(), "Cup", DateTime.UtcNow, null, null, null, null, null),
            new MatchAnalysisTeamsResponse(team, team, team, team),
            new MatchAnalysisSportResponse(Guid.NewGuid(), "Water Polo", "WP"),
            new MatchAnalysisSportConfigurationResponse(Guid.NewGuid(), true, 4, 8, "25x20", 15, 15, 7, 25m, 20m, 3, "<svg/>", null),
            [],
            []);
    }
}
