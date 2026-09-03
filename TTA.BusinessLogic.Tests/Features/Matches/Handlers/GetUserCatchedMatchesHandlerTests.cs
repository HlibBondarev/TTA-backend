using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="GetUserCatchedMatchesHandler"/>.
/// </summary>
public class GetUserCatchedMatchesHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<ILogger<GetUserCatchedMatchesHandler>> _loggerMock = new();
    private readonly GetUserCatchedMatchesHandler _handler;

    public GetUserCatchedMatchesHandlerTests()
    {
        _handler = new GetUserCatchedMatchesHandler(_matchRepositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="GetUserCatchedMatchesHandler"/> retrieves tracked match projections 
    /// and maps them accurately to response DTOs.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedResponses_WhenProjectionsExist()
    {
        // Arrange
        var userId = "user-123";
        var query = new GetUserCatchedMatchesQuery(userId);

        var projections = new List<MatchWithDetailsProjection>
        {
            new(
                Id: Guid.NewGuid(),
                TournamentId: Guid.NewGuid(),
                TournamentName: "Training Tournament",
                HomeTeamId: Guid.NewGuid(),
                HomeTeamName: "Home Squad",
                GuestTeamId: Guid.NewGuid(),
                GuestTeamName: "Opponent Squad",
                ScheduledAt: DateTime.UtcNow,
                MatchNumber: "M-1",
                Venue: "Main Pool",
                Temperature: 24.5,
                HomeScore: 5,
                GuestScore: 3,
                CreatedAt: DateTime.UtcNow.AddHours(-1),
                TrackedTeamId: Guid.NewGuid()
            )
        };

        _matchRepositoryMock
            .Setup(r => r.GetCatchedMatchesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);

        var response = result[0];
        var proj = projections[0];

        response.Id.Should().Be(proj.Id);
        response.TournamentId.Should().Be(proj.TournamentId);
        response.TournamentName.Should().Be(proj.TournamentName);
        response.HomeTeamId.Should().Be(proj.HomeTeamId);
        response.HomeTeamName.Should().Be(proj.HomeTeamName);
        response.GuestTeamId.Should().Be(proj.GuestTeamId);
        response.GuestTeamName.Should().Be(proj.GuestTeamName);
        response.ScheduledAt.Should().Be(proj.ScheduledAt);
        response.MatchNumber.Should().Be(proj.MatchNumber);
        response.Venue.Should().Be(proj.Venue);
        response.Temperature.Should().Be(proj.Temperature);
        response.HomeScore.Should().Be(proj.HomeScore);
        response.GuestScore.Should().Be(proj.GuestScore);
        response.CreatedAt.Should().Be(proj.CreatedAt);
        response.TrackedTeamId.Should().Be(proj.TrackedTeamId);

        _matchRepositoryMock.Verify(r => r.GetCatchedMatchesByUserIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetUserCatchedMatchesHandler"/> returns an empty collection 
    /// when the user tracks no matches.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnEmptyCollection_WhenNoProjectionsExist()
    {
        // Arrange
        var userId = "user-123";
        var query = new GetUserCatchedMatchesQuery(userId);

        _matchRepositoryMock
            .Setup(r => r.GetCatchedMatchesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<MatchWithDetailsProjection>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}