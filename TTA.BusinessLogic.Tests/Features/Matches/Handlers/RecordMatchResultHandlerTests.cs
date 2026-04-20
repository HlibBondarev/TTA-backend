using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Matches.Commands;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="RecordMatchResultHandler"/> ensuring correct 
/// match result updates and existence validation.
/// </summary>
public class RecordMatchResultHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<RecordMatchResultHandler>> _loggerMock;
    private readonly RecordMatchResultHandler _handler;

    public RecordMatchResultHandlerTests()
    {
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<RecordMatchResultHandler>>();
        _handler = new RecordMatchResultHandler(_matchRepositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully updates an existing match 
    /// with new scores and weather data.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldUpdateMatch_WhenMatchExists()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var existingMatch = new Match
        {
            Id = matchId,
            MatchNumber = "M-101",
            TournamentId = Guid.NewGuid()
        };

        var command = new RecordMatchResultCommand(
            MatchId: matchId,
            HomeScore: 2,
            GuestScore: 1,
            Temperature: 18.5
        );

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMatch);

        _matchRepositoryMock
            .Setup(r => r.UpsertMatchAsync(It.IsAny<Match>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match m, CancellationToken _) => m);

        // Act
        var resultId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        resultId.Should().Be(matchId);

        // Verify mapping via SetToModel logic
        _matchRepositoryMock.Verify(r => r.UpsertMatchAsync(It.Is<Match>(m =>
            m.HomeScore == 2 &&
            m.GuestScore == 1 &&
            m.Temperature == 18.5), It.IsAny<CancellationToken>()), Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully updated match")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when attempting 
    /// to record results for a non-existent match.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowNotFoundException_WhenMatchDoesNotExist()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var command = new RecordMatchResultCommand(matchId, 0, 0, null);

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {matchId} was not found.");

        _matchRepositoryMock.Verify(r => r.UpsertMatchAsync(It.IsAny<Match>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}