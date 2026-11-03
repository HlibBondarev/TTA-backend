using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetGameEventByIdHandler"/> class.
/// Ensures correct mapping of repository results to DTOs and proper error handling.
/// </summary>
public class GetGameEventByIdHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<ILogger<GetGameEventByIdHandler>> _loggerMock;
    private readonly GetGameEventByIdHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetGameEventByIdHandlerTests"/> class.
    /// </summary>
    public GetGameEventByIdHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _loggerMock = new Mock<ILogger<GetGameEventByIdHandler>>();

        _handler = new GetGameEventByIdHandler(
            _gameEventRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown 
    /// when the requested game event does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_EventDoesNotExist()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var query = new GetGameEventByIdQuery(eventId);

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameEventProjection?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Game event with ID {eventId} not found.");

        _gameEventRepositoryMock.Verify(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns a correctly populated response DTO
    /// including spatial field coordinates and shot frame coordinates when the event exists.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnEventResponse_When_EventExists()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var query = new GetGameEventByIdQuery(eventId);
        GameEventProjection detailedEvent = new
        (
            Id: eventId,
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            EventName: "Goal",
            IsPositive: true,
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            NormalizedMatchTime: TimeSpan.FromMinutes(15),
            IsLeadToGoal: false,
            LocationX: 45.50m,
            LocationY: 82.25m,
            ShotLocationX: 12.30m,
            ShotLocationY: 45.60m,
            PlayerName: "John Doe",
            PlayerNumber: 10,
            TeamId: Guid.NewGuid(),
            TeamName: "Warriors"
        );

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailedEvent);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(eventId);
        result.EventName.Should().Be("Goal");
        result.PlayerName.Should().Be("John Doe");
        result.TeamName.Should().Be("Warriors");
        result.LocationX.Should().Be(45.50m);
        result.LocationY.Should().Be(82.25m);
        result.ShotLocationX.Should().Be(12.30m);
        result.ShotLocationY.Should().Be(45.60m);

        _gameEventRepositoryMock.Verify(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetGameEventByIdHandler.Handle"/> correctly maps shot frame coordinates 
    /// (<c>ShotLocationX</c> and <c>ShotLocationY</c>) when present in the projection.
    /// </summary>
    [Fact]
    public async Task Handle_Should_MapShotCoordinates_When_PresentInProjection()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var query = new GetGameEventByIdQuery(eventId);
        GameEventProjection detailedEvent = new
        (
            Id: eventId,
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            EventName: "Shot on Target",
            IsPositive: true,
            PeriodNumber: 2,
            EventTimestamp: DateTime.UtcNow,
            NormalizedMatchTime: TimeSpan.FromMinutes(8),
            IsLeadToGoal: false,
            LocationX: 15.00m,
            LocationY: 25.00m,
            ShotLocationX: 5.50m,
            ShotLocationY: 3.25m,
            PlayerName: "Alex Smith",
            PlayerNumber: 7,
            TeamId: Guid.NewGuid(),
            TeamName: "Dolphins"
        );

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailedEvent);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ShotLocationX.Should().Be(5.50m, "shot target X coordinate must be mapped to DTO");
        result.ShotLocationY.Should().Be(3.25m, "shot target Y coordinate must be mapped to DTO");
    }

    /// <summary>
    /// Verifies that spatial field coordinates and shot frame coordinates are mapped to null when projection coordinates are null.
    /// </summary>
    [Fact]
    public async Task Handle_Should_MapNullCoordinates_When_CoordinatesAreNullInProjection()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var query = new GetGameEventByIdQuery(eventId);
        GameEventProjection detailedEvent = new
        (
            Id: eventId,
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            EventName: "Foul",
            IsPositive: false,
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            NormalizedMatchTime: TimeSpan.FromMinutes(5),
            IsLeadToGoal: false,
            LocationX: null,
            LocationY: null,
            ShotLocationX: null,
            ShotLocationY: null,
            PlayerName: "Jane Doe",
            PlayerNumber: 5,
            TeamId: Guid.NewGuid(),
            TeamName: "Warriors"
        );

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailedEvent);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.LocationX.Should().BeNull();
        result.LocationY.Should().BeNull();
        result.ShotLocationX.Should().BeNull();
        result.ShotLocationY.Should().BeNull();
    }
}