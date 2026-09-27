using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.EventDefinitions.Handlers;
using TTA.BusinessLogic.Features.EventDefinitions.Queries;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;
using Match = TTA.DataAccess.Models.Match;


namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Handlers;

/// <summary>
/// Unit tests for <see cref="GetEventDefinitionsForMatchHandler"/>.
/// </summary>
public class GetEventDefinitionsForMatchHandlerTests
{
    private readonly Mock<IEventDefinitionRepository> _eventDefinitionRepositoryMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<GetEventDefinitionsForMatchHandler>> _loggerMock;
    private readonly GetEventDefinitionsForMatchHandler _handler;

    public GetEventDefinitionsForMatchHandlerTests()
    {
        _eventDefinitionRepositoryMock = new Mock<IEventDefinitionRepository>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<GetEventDefinitionsForMatchHandler>>();
        _handler = new GetEventDefinitionsForMatchHandler(
            _eventDefinitionRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="GetEventDefinitionsForMatchHandler.Handle"/> returns mapped event definition responses
    /// enriched with custom and preset metadata when a user identifier is provided.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedDefinitions_WhenUserIsProvided()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var userId = "auth0|69cf7ec5eff8f1358a0b9ae0";

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Match { Id = matchId });

        var mockProjections = new List<UserEventDefinitionProjection>
        {
            new(
                Id: Guid.NewGuid(),
                SportId: sportId,
                Name: "Goal",
                ShortName: "GOAL",
                IsPositive: true,
                IsCustom: false,
                IsEnabled: true,
                SortOrder: 1
            ),
            new(
                Id: Guid.NewGuid(),
                SportId: sportId,
                Name: "Custom TTA",
                ShortName: "CTTA",
                IsPositive: true,
                IsCustom: true,
                IsEnabled: false, // Configured IsEnabled to false for metadata assertion coverage
                SortOrder: 2
            )
        };

        _eventDefinitionRepositoryMock
            .Setup(r => r.GetMatchEventDefinitionsAsync(matchId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockProjections);

        var query = new GetEventDefinitionsForMatchQuery(matchId, userId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var list = result.ToList();
        list.Should().HaveCount(2);

        list[0].Name.Should().Be("Goal");
        list[0].IsCustom.Should().BeFalse();
        list[0].IsEnabled.Should().BeTrue();
        list[0].SortOrder.Should().Be(1);

        list[1].Name.Should().Be("Custom TTA");
        list[1].IsCustom.Should().BeTrue();
        list[1].IsEnabled.Should().BeFalse();
        list[1].SortOrder.Should().Be(2);

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _eventDefinitionRepositoryMock.Verify(r => r.GetMatchEventDefinitionsAsync(matchId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetEventDefinitionsForMatchHandler.Handle"/> retrieves active system default definitions
    /// when the user identifier is null.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnSystemDefaults_WhenUserIdIsNull()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var sportId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Match { Id = matchId });

        var mockProjections = new List<UserEventDefinitionProjection>
        {
            new(
                Id: Guid.NewGuid(),
                SportId: sportId,
                Name: "Goal",
                ShortName: "GOAL",
                IsPositive: true,
                IsCustom: false,
                IsEnabled: true,
                SortOrder: 1
            )
        };

        _eventDefinitionRepositoryMock
            .Setup(r => r.GetMatchEventDefinitionsAsync(matchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockProjections);

        var query = new GetEventDefinitionsForMatchQuery(matchId, null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var list = result.ToList();
        list.Should().HaveCount(1);
        list[0].Name.Should().Be("Goal");

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _eventDefinitionRepositoryMock.Verify(r => r.GetMatchEventDefinitionsAsync(matchId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetEventDefinitionsForMatchHandler.Handle"/> returns an empty collection
    /// when no definitions are returned from the repository layer.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnEmptyCollection_WhenNoDefinitionsFound()
    {
        // Arrange
        var matchId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Match { Id = matchId });

        _eventDefinitionRepositoryMock
            .Setup(r => r.GetMatchEventDefinitionsAsync(matchId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<UserEventDefinitionProjection>());

        var query = new GetEventDefinitionsForMatchQuery(matchId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
        _eventDefinitionRepositoryMock.Verify(r => r.GetMatchEventDefinitionsAsync(matchId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetEventDefinitionsForMatchHandler.Handle"/> throws <see cref="KeyNotFoundException"/>
    /// when the specified match identifier does not exist in the database.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowKeyNotFoundException_WhenMatchDoesNotExist()
    {
        // Arrange
        var nonExistentMatchId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(nonExistentMatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        var query = new GetEventDefinitionsForMatchQuery(nonExistentMatchId);

        // Act
        Func<Task> act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"Match with ID '{nonExistentMatchId}' was not found.");

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(nonExistentMatchId, It.IsAny<CancellationToken>()), Times.Once);
        _eventDefinitionRepositoryMock.Verify(r => r.GetMatchEventDefinitionsAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that <see cref="GetEventDefinitionsForMatchHandler.Handle"/> passes the provided
    /// <see cref="CancellationToken"/> directly to repository method calls.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPassCancellationTokenToRepository()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(matchId, cancellationToken))
            .ReturnsAsync(new Match { Id = matchId });

        _eventDefinitionRepositoryMock
            .Setup(r => r.GetMatchEventDefinitionsAsync(matchId, null, cancellationToken))
            .ReturnsAsync(Enumerable.Empty<UserEventDefinitionProjection>());

        var query = new GetEventDefinitionsForMatchQuery(matchId);

        // Act
        await _handler.Handle(query, cancellationToken);

        // Assert
        _matchRepositoryMock.Verify(r => r.GetByIdAsync(matchId, cancellationToken), Times.Once);
        _eventDefinitionRepositoryMock.Verify(r => r.GetMatchEventDefinitionsAsync(matchId, null, cancellationToken), Times.Once);
    }
}