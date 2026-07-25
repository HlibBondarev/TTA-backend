using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.EventDefinitions.Handlers;
using TTA.BusinessLogic.Features.EventDefinitions.Queries;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions;

public class GetEventDefinitionsForMatchHandlerTests
{
    private readonly Mock<IEventDefinitionRepository> _repositoryMock;
    private readonly Mock<ILogger<GetEventDefinitionsForMatchHandler>> _loggerMock;
    private readonly GetEventDefinitionsForMatchHandler _handler;

    public GetEventDefinitionsForMatchHandlerTests()
    {
        _repositoryMock = new Mock<IEventDefinitionRepository>();
        _loggerMock = new Mock<ILogger<GetEventDefinitionsForMatchHandler>>();
        _handler = new GetEventDefinitionsForMatchHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnMappedDefinitions_WhenMatchExists()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var mockDefinitions = new List<EventDefinition>
        {
            new()
            {
                Id = Guid.NewGuid(),
                SportId = sportId,
                Name = "Goal",
                ShortName = "G",
                IsPositive = true,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                SportId = sportId,
                Name = "Foul",
                ShortName = "F",
                IsPositive = false,
                CreatedAt = DateTime.UtcNow
            }
        };

        _repositoryMock
            .Setup(r => r.GetMatchEventDefinitionsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockDefinitions);

        var query = new GetEventDefinitionsForMatchQuery(matchId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var list = result.ToList();
        list.Should().HaveCount(2);

        list[0].Name.Should().Be("Goal");
        list[0].IsPositive.Should().BeTrue();
        list[1].Name.Should().Be("Foul");
        list[1].IsPositive.Should().BeFalse();

        _repositoryMock.Verify(r => r.GetMatchEventDefinitionsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyCollection_WhenNoDefinitionsFound()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetMatchEventDefinitionsAsync(matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<EventDefinition>());

        var query = new GetEventDefinitionsForMatchQuery(matchId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _repositoryMock.Verify(r => r.GetMatchEventDefinitionsAsync(matchId, It.IsAny<CancellationToken>()), Times.Once);
    }
}