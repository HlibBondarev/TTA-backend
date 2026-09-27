using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.PlayerPresences.Handlers;
using TTA.BusinessLogic.Features.PlayerPresences.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetMatchPresenceHandler"/> class.
/// Validates match existence verification and the mapping logic from raw repository entities to DTOs.
/// </summary>
public class GetMatchPresenceHandlerTests
{
    private readonly Mock<IPlayerPresenceRepository> _playerPresenceRepositoryMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<GetMatchPresenceHandler>> _loggerMock;
    private readonly GetMatchPresenceHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetMatchPresenceHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public GetMatchPresenceHandlerTests()
    {
        _playerPresenceRepositoryMock = new Mock<IPlayerPresenceRepository>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<GetMatchPresenceHandler>>();

        _handler = new GetMatchPresenceHandler(
            _playerPresenceRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a successfully mapped collection of response DTOs 
    /// when the match exists and data is retrieved.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnMappedTimeline_When_MatchExists()
    {
        // Arrange
        var query = new GetMatchPresenceQuery(Guid.NewGuid());
        var match = new Match { Id = query.MatchId };

        var rawPresences = new List<PlayerPresence>
        {
            CreateRawPresence(Guid.NewGuid(), 1, DateTime.UtcNow.AddMinutes(-20), DateTime.UtcNow.AddMinutes(-10)),
            CreateRawPresence(Guid.NewGuid(), 2, DateTime.UtcNow.AddMinutes(-5), null)
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(query.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _playerPresenceRepositoryMock
            .Setup(r => r.GetMatchPresenceAsync(query.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawPresences);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        var responseList = result.ToList();
        responseList.Count.Should().Be(2);

        // Validate mapping for the first record
        responseList[0].Id.Should().Be(rawPresences[0].Id);
        responseList[0].MatchLineupId.Should().Be(rawPresences[0].MatchLineupId);
        responseList[0].PeriodNumber.Should().Be(rawPresences[0].PeriodNumber);
        responseList[0].TimeIn.Should().Be(rawPresences[0].TimeIn);
        responseList[0].TimeOut.Should().Be(rawPresences[0].TimeOut);

        // Validate mapping for the second record (active player)
        responseList[1].PeriodNumber.Should().Be(rawPresences[1].PeriodNumber);
        responseList[1].TimeOut.Should().BeNull();

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(query.MatchId, It.IsAny<CancellationToken>()), Times.Once);
        _playerPresenceRepositoryMock.Verify(r => r.GetMatchPresenceAsync(query.MatchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when the specified match does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ThrowNotFoundException_When_MatchDoesNotExist()
    {
        // Arrange
        var query = new GetMatchPresenceQuery(Guid.NewGuid());

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(query.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {query.MatchId} was not found.");

        _playerPresenceRepositoryMock.Verify(r => r.GetMatchPresenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Helper method to create a <see cref="PlayerPresence"/> entity with populated data for testing.
    /// </summary>
    /// <param name="id">The unique identifier of the presence record.</param>
    /// <param name="period">The match period number.</param>
    /// <param name="timeIn">The UTC timestamp when the player entered the field.</param>
    /// <param name="timeOut">The UTC timestamp when the player left the field, if applicable.</param>
    /// <returns>A configured PlayerPresence entity.</returns>
    private static PlayerPresence CreateRawPresence(
        Guid id,
        int period,
        DateTime timeIn,
        DateTime? timeOut)
    {
        return new PlayerPresence
        {
            Id = id,
            MatchLineupId = Guid.NewGuid(),
            PeriodNumber = period,
            TimeIn = timeIn,
            TimeOut = timeOut
        };
    }
}