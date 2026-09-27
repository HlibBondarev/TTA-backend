using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.Tournaments.Handlers;
using TTA.BusinessLogic.Features.Tournaments.Queries;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Tournaments.Handlers;

/// <summary>
/// Unit tests for <see cref="GetTournamentByIdHandler"/>.
/// </summary>
public class GetTournamentByIdHandlerTests
{
    private readonly Mock<ITournamentRepository> _repositoryMock;
    private readonly Mock<ILogger<GetTournamentByIdHandler>> _loggerMock;
    private readonly GetTournamentByIdHandler _handler;

    public GetTournamentByIdHandlerTests()
    {
        _repositoryMock = new Mock<ITournamentRepository>();
        _loggerMock = new Mock<ILogger<GetTournamentByIdHandler>>();
        _handler = new GetTournamentByIdHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns the tournament when the repository finds it.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnTournament_WhenExists()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var expectedTournament = new Tournament { Id = tournamentId, Name = "Test Tournament" };

        _repositoryMock.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedTournament);

        var query = new GetTournamentByIdQuery(tournamentId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(tournamentId);
        _repositoryMock.Verify(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns null when the tournament is not found in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnNull_WhenDoesNotExist()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        _repositoryMock.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament?)null);

        var query = new GetTournamentByIdQuery(tournamentId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }
}