using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Sports.Handlers;
using TTA.BusinessLogic.Features.Sports.Queries;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Sports.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetAllSportsHandler"/>.
/// </summary>
public class GetAllSportsHandlerTests
{
    private readonly Mock<ISportRepository> _sportRepoMock;
    private readonly Mock<ILogger<GetAllSportsHandler>> _loggerMock;
    private readonly GetAllSportsHandler _handler;

    public GetAllSportsHandlerTests()
    {
        _sportRepoMock = new Mock<ISportRepository>();
        _loggerMock = new Mock<ILogger<GetAllSportsHandler>>();

        _handler = new GetAllSportsHandler(_sportRepoMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that an empty collection is returned when no sports exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_NoSportsExist_ShouldReturnEmptyList()
    {
        // Arrange
        var query = new GetAllSportsQuery();

        _sportRepoMock
            .Setup(x => x.GetAllSportsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Empty(result);
        _sportRepoMock.Verify(x => x.GetAllSportsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler correctly maps all sport entities retrieved from the repository 
    /// into a collection of <see cref="SportResponse"/> DTOs.
    /// </summary>
    [Fact]
    public async Task Handle_SportsExist_ShouldReturnPopulatedList()
    {
        // Arrange
        var query = new GetAllSportsQuery();
        var sports = new List<Sport>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Water Polo",
                ShortName = "WP",
                DefaultConfigId = Guid.NewGuid()
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Football",
                ShortName = "FB",
                DefaultConfigId = Guid.NewGuid()
            }
        };

        _sportRepoMock
            .Setup(x => x.GetAllSportsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sports);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        var resultList = result.ToList();
        Assert.Equal(2, resultList.Count);

        Assert.Equal(sports[0].Id, resultList[0].Id);
        Assert.Equal("Water Polo", resultList[0].Name);
        Assert.Equal("WP", resultList[0].ShortName);
        Assert.Equal(sports[0].DefaultConfigId, resultList[0].DefaultConfigId);

        Assert.Equal(sports[1].Id, resultList[1].Id);
        Assert.Equal("Football", resultList[1].Name);
        Assert.Equal("FB", resultList[1].ShortName);
        Assert.Equal(sports[1].DefaultConfigId, resultList[1].DefaultConfigId);

        _sportRepoMock.Verify(x => x.GetAllSportsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}