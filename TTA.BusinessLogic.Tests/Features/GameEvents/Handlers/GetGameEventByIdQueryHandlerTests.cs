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
            .ReturnsAsync((GameEventProjection?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Game event with ID {eventId} not found.");

        _gameEventRepositoryMock.Verify(r => r.GetByIdWithDetailsAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
    }
}