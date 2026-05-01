using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.MatchLineups.Handlers;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetMatchLineupHandler"/> ensuring proper retrieval 
/// and mapping of the full match protocol from dynamic database results.
/// </summary>
public class GetMatchLineupHandlerTests
{
    private readonly Mock<IMatchLineupRepository> _matchLineupRepoMock;
    private readonly Mock<IMatchRepository> _matchRepoMock; // Додано Mock для MatchRepository
    private readonly Mock<ILogger<GetMatchLineupHandler>> _loggerMock;
    private readonly GetMatchLineupHandler _handler;

    public GetMatchLineupHandlerTests()
    {
        _matchLineupRepoMock = new Mock<IMatchLineupRepository>();
        _matchRepoMock = new Mock<IMatchRepository>(); // Ініціалізація
        _loggerMock = new Mock<ILogger<GetMatchLineupHandler>>();

        // Тепер передаємо всі ТРИ параметри, як того вимагає конструктор хендлера
        _handler = new GetMatchLineupHandler(
            _matchLineupRepoMock.Object,
            _matchRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that when a match exists and has players, the handler returns mapped DTOs.
    /// </summary>
    [Fact]
    public async Task Handle_MatchExistsWithPlayers_ShouldReturnMappedCollection()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchLineupQuery(matchId);

        // 1. Налаштовуємо успішну перевірку існування матчу
        _matchRepoMock
            .Setup(x => x.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Match { Id = matchId });

        var mockList = new List<object>();
        dynamic player = new ExpandoObject();
        player.id = Guid.NewGuid();
        player.matchid = matchId;
        player.playerrosterid = Guid.NewGuid();
        player.teamid = Guid.NewGuid();
        player.firstname = "John";
        player.lastname = "Doe";
        player.number = 10;
        player.isinstartinglineup = true;
        player.positionid = Guid.NewGuid();
        player.positionname = "Forward";
        mockList.Add(player);

        _matchLineupRepoMock
            .Setup(x => x.GetByMatchIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockList.Cast<dynamic>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.First().FirstName.Should().Be("John");
    }

    /// <summary>
    /// Verifies that if the match does not exist, a NotFoundException is thrown.
    /// </summary>
    [Fact]
    public async Task Handle_MatchNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var query = new GetMatchLineupQuery(matchId);

        _matchRepoMock
            .Setup(x => x.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
        _matchLineupRepoMock.Verify(x => x.GetByMatchIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}