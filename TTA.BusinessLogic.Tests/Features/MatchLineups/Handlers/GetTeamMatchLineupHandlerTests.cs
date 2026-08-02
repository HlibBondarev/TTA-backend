using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.MatchLineups.Handlers;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Handlers;

/// <summary>
/// Unit tests for <see cref="GetTeamMatchLineupHandler"/>.
/// </summary>
public class GetTeamMatchLineupHandlerTests
{
    private readonly Mock<IMatchLineupRepository> _matchLineupRepositoryMock = new();
    private readonly Mock<IMatchRepository> _matchRepositoryMock = new();
    private readonly Mock<ILogger<GetTeamMatchLineupHandler>> _loggerMock = new();

    private readonly GetTeamMatchLineupHandler _handler;

    public GetTeamMatchLineupHandlerTests()
    {
        _handler = new GetTeamMatchLineupHandler(
            _matchLineupRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that handler successfully retrieves and maps lineup projections when match and team participation are valid.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedLineup_WhenMatchAndTeamAreValid()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var match = new Match
        {
            Id = matchId,
            HomeTeamId = teamId,
            GuestTeamId = guestId
        };

        var projections = new List<MatchLineupProjection>
        {
            new (
                Guid.NewGuid(),
                matchId,
                teamId,
                Guid.NewGuid(),
                "Hlib",
                "Bondarev",
                10,
                Guid.NewGuid(),
                "Forward")
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _matchLineupRepositoryMock
            .Setup(r => r.GetTeamMatchLineupAsync(matchId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        var query = new GetTeamMatchLineupQuery(matchId, teamId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var list = result.ToList();
        list.Should().HaveCount(1);
        list[0].FirstName.Should().Be("Hlib");
        list[0].LastName.Should().Be("Bondarev");
        list[0].Number.Should().Be(10);
    }

    /// <summary>
    /// Verifies that handler throws <see cref="NotFoundException"/> when the requested match does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenMatchDoesNotExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        var query = new GetTeamMatchLineupQuery(matchId, teamId);

        // Act
        var act = () => _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {matchId} was not found.");
    }

    /// <summary>
    /// Verifies that handler throws <see cref="NotFoundException"/> when team is not a participant in the match.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenTeamIsNotParticipant()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var nonParticipantTeamId = Guid.NewGuid();

        var match = new Match
        {
            Id = matchId,
            HomeTeamId = Guid.NewGuid(),
            GuestTeamId = Guid.NewGuid()
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        var query = new GetTeamMatchLineupQuery(matchId, nonParticipantTeamId);

        // Act
        var act = () => _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Team with ID {nonParticipantTeamId} is not a participant in Match {matchId}.");
    }
}