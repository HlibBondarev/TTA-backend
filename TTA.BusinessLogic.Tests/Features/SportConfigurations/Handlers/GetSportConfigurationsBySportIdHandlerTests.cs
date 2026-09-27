using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.SportConfigurations.Handlers;
using TTA.BusinessLogic.Features.SportConfigurations.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.SportConfigurations.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetSportConfigurationsBySportIdHandler"/>.
/// </summary>
public class GetSportConfigurationsBySportIdHandlerTests
{
    private readonly Mock<ISportConfigurationRepository> _sportConfigurationRepoMock;
    private readonly Mock<ILogger<GetSportConfigurationsBySportIdHandler>> _loggerMock;
    private readonly GetSportConfigurationsBySportIdHandler _handler;

    public GetSportConfigurationsBySportIdHandlerTests()
    {
        _sportConfigurationRepoMock = new Mock<ISportConfigurationRepository>();
        _loggerMock = new Mock<ILogger<GetSportConfigurationsBySportIdHandler>>();

        _handler = new GetSportConfigurationsBySportIdHandler(
            _sportConfigurationRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when no configurations are found for the given sport ID.
    /// </summary>
    [Fact]
    public async Task Handle_ConfigurationsDoNotExist_ShouldThrowNotFoundException()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var query = new GetSportConfigurationsBySportIdQuery(sportId);

        _sportConfigurationRepoMock
            .Setup(x => x.GetBySportIdAsync(sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.Handle(query, CancellationToken.None));

        Assert.Contains(sportId.ToString(), exception.Message);

        _sportConfigurationRepoMock.Verify(x => x.GetBySportIdAsync(sportId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler correctly maps configuration entities into a collection 
    /// of <see cref="SportConfigurationResponse"/> DTOs when configurations exist.
    /// </summary>
    [Fact]
    public async Task Handle_ConfigurationsExist_ShouldReturnPopulatedList()
    {
        // Arrange
        var sportId = Guid.NewGuid();
        var query = new GetSportConfigurationsBySportIdQuery(sportId);

        var configs = new List<SportConfiguration>
        {
            new()
            {
                Id = Guid.NewGuid(),
                SportId = sportId,
                UsesCleanTime = true,
                PeriodsCount = 4,
                PeriodDurationMinutes = 8,
                FieldSize = "30x20m",
                RosterLimit = 15,
                LineupLimit = 13,
                ActivePlayersLimit = 7
            }
        };

        _sportConfigurationRepoMock
            .Setup(x => x.GetBySportIdAsync(sportId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        var resultList = result.ToList();
        Assert.Single(resultList);

        var firstConfig = resultList[0];
        Assert.Equal(configs[0].Id, firstConfig.Id);
        Assert.Equal(sportId, firstConfig.SportId);
        Assert.True(firstConfig.UsesCleanTime);
        Assert.Equal(4, firstConfig.PeriodsCount);
        Assert.Equal(8, firstConfig.PeriodDurationMinutes);
        Assert.Equal("30x20m", firstConfig.FieldSize);
        Assert.Equal(15, firstConfig.RosterLimit);
        Assert.Equal(13, firstConfig.LineupLimit);
        Assert.Equal(7, firstConfig.ActivePlayersLimit);

        _sportConfigurationRepoMock.Verify(x => x.GetBySportIdAsync(sportId, It.IsAny<CancellationToken>()), Times.Once);
    }
}