using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.EventDefinitions.Handlers;
using TTA.BusinessLogic.Features.EventDefinitions.Queries;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Handlers;

/// <summary>
/// Unit tests for <see cref="GetAvailableEventDefinitionsHandler"/>.
/// </summary>
public class GetAvailableEventDefinitionsHandlerTests
{
    private readonly Mock<IEventDefinitionRepository> _repositoryMock;
    private readonly Mock<ILogger<GetAvailableEventDefinitionsHandler>> _loggerMock;
    private readonly GetAvailableEventDefinitionsHandler _handler;

    public GetAvailableEventDefinitionsHandlerTests()
    {
        _repositoryMock = new Mock<IEventDefinitionRepository>();
        _loggerMock = new Mock<ILogger<GetAvailableEventDefinitionsHandler>>();
        _handler = new GetAvailableEventDefinitionsHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="GetAvailableEventDefinitionsHandler.Handle"/> returns mapped event definition responses
    /// enriched with user preset state and layout sort order when repository projections exist.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedEventDefinitions_WhenProjectionsExist()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var userId = "auth0|user123";

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
                SortOrder: 0
            ),
            new(
                Id: Guid.NewGuid(),
                SportId: sportId,
                Name: "Custom Block",
                ShortName: "C-BLK",
                IsPositive: true,
                IsCustom: true,
                IsEnabled: false,
                SortOrder: 1
            )
        };

        _repositoryMock
            .Setup(r => r.GetAvailableForUserAsync(userId, sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockProjections);

        var query = new GetAvailableEventDefinitionsQuery(sportId, userId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var list = result.ToList();
        list.Should().HaveCount(2);

        list[0].Id.Should().Be(mockProjections[0].Id);
        list[0].SportId.Should().Be(sportId);
        list[0].Name.Should().Be("Goal");
        list[0].ShortName.Should().Be("GOAL");
        list[0].IsPositive.Should().BeTrue();
        list[0].IsCustom.Should().BeFalse();
        list[0].IsEnabled.Should().BeTrue();
        list[0].SortOrder.Should().Be(0);

        list[1].Id.Should().Be(mockProjections[1].Id);
        list[1].SportId.Should().Be(sportId);
        list[1].Name.Should().Be("Custom Block");
        list[1].ShortName.Should().Be("C-BLK");
        list[1].IsPositive.Should().BeTrue();
        list[1].IsCustom.Should().BeTrue();
        list[1].IsEnabled.Should().BeFalse();
        list[1].SortOrder.Should().Be(1);

        _repositoryMock.Verify(r => r.GetAvailableForUserAsync(userId, sportId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetAvailableEventDefinitionsHandler.Handle"/> returns an empty collection 
    /// when no event definition projections are returned from the repository.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnEmptyCollection_WhenNoProjectionsFound()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var userId = "auth0|user123";

        _repositoryMock
            .Setup(r => r.GetAvailableForUserAsync(userId, sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<UserEventDefinitionProjection>());

        var query = new GetAvailableEventDefinitionsQuery(sportId, userId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _repositoryMock.Verify(r => r.GetAvailableForUserAsync(userId, sportId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetAvailableEventDefinitionsHandler.Handle"/> passes the provided
    /// <see cref="CancellationToken"/> directly to the repository method call.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPassCancellationTokenToRepository()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var userId = "auth0|user123";

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _repositoryMock
            .Setup(r => r.GetAvailableForUserAsync(userId, sportId, cancellationToken))
            .ReturnsAsync(Enumerable.Empty<UserEventDefinitionProjection>());

        var query = new GetAvailableEventDefinitionsQuery(sportId, userId);

        // Act
        await _handler.Handle(query, cancellationToken);

        // Assert
        _repositoryMock.Verify(r => r.GetAvailableForUserAsync(userId, sportId, cancellationToken), Times.Once);
    }
}