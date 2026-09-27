using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="GetMatchByIdWithDetailsHandler"/> verifying single match 
/// retrieval logic and dynamic-to-DTO mapping accuracy.
/// </summary>
public class GetMatchByIdWithDetailsHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<GetMatchByIdWithDetailsHandler>> _loggerMock;
    private readonly GetMatchByIdWithDetailsHandler _handler;

    public GetMatchByIdWithDetailsHandlerTests()
    {
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<GetMatchByIdWithDetailsHandler>>();
        _handler = new GetMatchByIdWithDetailsHandler(_matchRepositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler correctly maps a single dynamic database row 
    /// to a <see cref="MatchWithDetailsResponse"/> when the match exists.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedResponse_WhenMatchExists()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchByIdWithDetailsQuery(matchId);

        // Simulating the dynamic result from GetMatchByIdWithDetailsAsync
        dynamic dynamicMatch = new ExpandoObject();
        dynamicMatch.id = matchId;
        dynamicMatch.tournamentid = Guid.NewGuid();
        dynamicMatch.tournamentname = "Summer League";
        dynamicMatch.hometeamid = Guid.NewGuid();
        dynamicMatch.hometeamname = "Warriors";
        dynamicMatch.guestteamid = Guid.NewGuid();
        dynamicMatch.guestteamname = "Titans";
        dynamicMatch.scheduledat = DateTime.UtcNow.AddDays(2);
        dynamicMatch.matchnumber = "MATCH-777";
        dynamicMatch.venue = "Olympic Stadium";
        dynamicMatch.temperature = 24.5;
        dynamicMatch.homescore = 2;
        dynamicMatch.guestscore = 1;
        dynamicMatch.createdat = DateTime.UtcNow;

        _matchRepositoryMock
            .Setup(r => r.GetMatchByIdWithDetailsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)dynamicMatch);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(matchId);
        result.MatchNumber.Should().Be("MATCH-777");
        result.HomeTeamName.Should().Be("Warriors");
        result.GuestTeamName.Should().Be("Titans");
        result.HomeScore.Should().Be(2);

        _matchRepositoryMock.Verify(r => r.GetMatchByIdWithDetailsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when the repository 
    /// returns null for the specified match identifier.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenMatchDoesNotExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchByIdWithDetailsQuery(matchId);

        _matchRepositoryMock
            .Setup(r => r.GetMatchByIdWithDetailsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {matchId} was not found.");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Match with ID {matchId} was not found.")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
