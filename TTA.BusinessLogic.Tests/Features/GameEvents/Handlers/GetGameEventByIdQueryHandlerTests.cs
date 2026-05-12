using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.GameEvents.Handlers;
using TTA.BusinessLogic.Features.GameEvents.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetGameEventByIdQueryHandler"/> class.
/// Ensures correct mapping of repository results to DTOs and proper error handling.
/// </summary>
public class GetGameEventByIdQueryHandlerTests
{
    private readonly Mock<IGameEventRepository> _gameEventRepositoryMock;
    private readonly Mock<ILogger<GetGameEventByIdQueryHandler>> _loggerMock;
    private readonly GetGameEventByIdQueryHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetGameEventByIdQueryHandlerTests"/> class.
    /// </summary>
    public GetGameEventByIdQueryHandlerTests()
    {
        _gameEventRepositoryMock = new Mock<IGameEventRepository>();
        _loggerMock = new Mock<ILogger<GetGameEventByIdQueryHandler>>();

        _handler = new GetGameEventByIdQueryHandler(
            _gameEventRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a correctly populated response DTO
    /// when the event exists. Uses ExpandoObject to support dynamic mapping.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnEventResponse_When_EventExists()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var query = new GetGameEventByIdQuery(eventId);

        // We use ExpandoObject because anonymous types are internal and 
        // not accessible via 'dynamic' across different assemblies.
        dynamic detailedEvent = new ExpandoObject();
        detailedEvent.id = eventId;
        detailedEvent.matchlineupid = Guid.NewGuid();
        detailedEvent.eventdefinitionid = Guid.NewGuid();
        detailedEvent.eventname = "Goal";
        detailedEvent.ispositive = true;
        detailedEvent.periodnumber = 1;
        detailedEvent.eventtimestamp = DateTime.UtcNow;
        detailedEvent.normalizedmatchtime = TimeSpan.FromMinutes(15);
        detailedEvent.isleadtogoal = false;
        detailedEvent.playername = "John Doe";
        detailedEvent.playernumber = 10;
        detailedEvent.teamid = Guid.NewGuid();
        detailedEvent.teamname = "Warriors";

        _gameEventRepositoryMock
            .Setup(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)detailedEvent);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(eventId);
        result.EventName.Should().Be("Goal");
        result.PlayerName.Should().Be("John Doe");
        result.TeamName.Should().Be("Warriors");

        _gameEventRepositoryMock.Verify(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
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
            .ReturnsAsync((object?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Game event with ID {eventId} not found.");

        _gameEventRepositoryMock.Verify(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
    }
}