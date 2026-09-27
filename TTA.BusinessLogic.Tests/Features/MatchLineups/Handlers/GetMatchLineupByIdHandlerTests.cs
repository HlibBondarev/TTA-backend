using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.MatchLineups.Handlers;
using TTA.BusinessLogic.Features.MatchLineups.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetMatchLineupByIdHandler"/> ensures that data 
/// is correctly retrieved, mapped from dynamic DB results, and handles missing records.
/// </summary>
public class GetMatchLineupByIdHandlerTests
{
    private readonly Mock<IMatchLineupRepository> _matchLineupRepoMock;
    private readonly Mock<ILogger<GetMatchLineupByIdHandler>> _loggerMock;
    private readonly GetMatchLineupByIdHandler _handler;

    public GetMatchLineupByIdHandlerTests()
    {
        _matchLineupRepoMock = new Mock<IMatchLineupRepository>();
        _loggerMock = new Mock<ILogger<GetMatchLineupByIdHandler>>();

        _handler = new GetMatchLineupByIdHandler(
            _matchLineupRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that when a lineup entry exists, the handler returns a correctly mapped response.
    /// </summary>
    [Fact]
    public async Task Handle_ExistingEntry_ShouldReturnMappedResponse()
    {
        // Arrange
        var query = new GetMatchLineupByIdQuery(Guid.NewGuid());

        // Creating a dynamic object to simulate the database join result
        dynamic mockResult = new ExpandoObject();
        mockResult.id = query.Id;
        mockResult.matchid = Guid.NewGuid();
        mockResult.playerrosterid = Guid.NewGuid();
        mockResult.teamid = Guid.NewGuid();
        mockResult.firstname = "John";
        mockResult.lastname = "Doe";
        mockResult.number = 10;
        mockResult.isinstartinglineup = true;
        mockResult.positionid = Guid.NewGuid();
        mockResult.positionname = "Forward";

        _matchLineupRepoMock
            .Setup(x => x.GetMatchLineupByIdWithDetailsAsync(query.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)mockResult);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(query.Id);
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.PositionName.Should().Be("Forward");
        _matchLineupRepoMock.Verify(x => x.GetMatchLineupByIdWithDetailsAsync(query.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the repository returns null.
    /// </summary>
    [Fact]
    public async Task Handle_NonExistentEntry_ShouldThrowNotFoundException()
    {
        // Arrange
        var query = new GetMatchLineupByIdQuery(Guid.NewGuid());

        _matchLineupRepoMock
            .Setup(x => x.GetMatchLineupByIdWithDetailsAsync(query.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match lineup entry with ID {query.Id} was not found.");
    }
}
