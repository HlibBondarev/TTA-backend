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
    /// Verifies that <see cref="GetAvailableEventDefinitionsHandler.Handle"/> returns fully mapped event definition responses
    /// enriched with user preset state and layout sort order when repository projections exist.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedEventDefinitions_WhenProjectionsExist()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var userId = "auth0|user123";
        var def1Id = Guid.NewGuid();
        var def2Id = Guid.NewGuid();

        var mockProjections = new List<UserEventDefinitionProjection>
        {
            new(
                Id: def1Id,
                SportId: sportId,
                Name: "Steal",
                ShortName: "ST",
                IsPositive: true,
                IsCustom: false,
                IsEnabled: true,
                SortOrder: 1
            ),
            new(
                Id: def2Id,
                SportId: sportId,
                Name: "Turnover",
                ShortName: "TO",
                IsPositive: false,
                IsCustom: true,
                IsEnabled: false,
                SortOrder: 2
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

        // Verify full field mapping for Item 0 (System positive action)
        list[0].Id.Should().Be(def1Id);
        list[0].SportId.Should().Be(sportId);
        list[0].Name.Should().Be("Steal");
        list[0].ShortName.Should().Be("ST");
        list[0].IsPositive.Should().BeTrue();
        list[0].IsCustom.Should().BeFalse();
        list[0].IsEnabled.Should().BeTrue();
        list[0].SortOrder.Should().Be(1);

        // Verify full field mapping for Item 1 (Custom negative action)
        list[1].Id.Should().Be(def2Id);
        list[1].SportId.Should().Be(sportId);
        list[1].Name.Should().Be("Turnover");
        list[1].ShortName.Should().Be("TO");
        list[1].IsPositive.Should().BeFalse();
        list[1].IsCustom.Should().BeTrue();
        list[1].IsEnabled.Should().BeFalse();
        list[1].SortOrder.Should().Be(2);

        _repositoryMock.Verify(r => r.GetAvailableForUserAsync(userId, sportId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GetAvailableEventDefinitionsHandler.Handle"/> preserves the exact projection sequence
    /// returned by the repository layer.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPreserveProjectionSequence_WhenMappedToDTOs()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var userId = "auth0|user123";

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        var mockProjections = new List<UserEventDefinitionProjection>
        {
            new(id1, sportId, "Action 1", "A1", true, false, true, 1),
            new(id2, sportId, "Action 2", "A2", true, false, true, 2),
            new(id3, sportId, "Action 3", "A3", false, false, false, 3)
        };

        _repositoryMock
            .Setup(r => r.GetAvailableForUserAsync(userId, sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockProjections);

        var query = new GetAvailableEventDefinitionsQuery(sportId, userId);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Select(x => x.Id).Should().ContainInConsecutiveOrder(id1, id2, id3);
        result.Select(x => x.SortOrder).Should().ContainInConsecutiveOrder(1, 2, 3);
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