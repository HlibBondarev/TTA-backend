using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Dynamic;
using TTA.BusinessLogic.Features.Matches.Handlers;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Matches.Handlers;

/// <summary>
/// Unit tests for <see cref="GetTournamentMatchesHandler"/> ensuring correct 
/// data retrieval and mapping from dynamic repository results.
/// </summary>
public class GetTournamentMatchesHandlerTests
{
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<GetTournamentMatchesHandler>> _loggerMock;
    private readonly GetTournamentMatchesHandler _handler;

    public GetTournamentMatchesHandlerTests()
    {
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<GetTournamentMatchesHandler>>();
        _handler = new GetTournamentMatchesHandler(_matchRepositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler correctly maps dynamic database rows to 
    /// a collection of <see cref="MatchWithDetailsResponse"/>.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnMappedResponses_WhenMatchesExist()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var query = new GetTournamentMatchesQuery(tournamentId);

        // Creating dynamic objects that simulate DapperRow from MatchRepository.GetByTournamentIdAsync[cite: 24, 37]
        var dynamicMatches = new List<dynamic>
        {
            CreateDynamicMatchRow(Guid.NewGuid(), tournamentId, "Autumn Cup", "Lions", "Tigers"),
            CreateDynamicMatchRow(Guid.NewGuid(), tournamentId, "Autumn Cup", "Bears", "Wolves")
        };

        _matchRepositoryMock
            .Setup(r => r.GetByTournamentIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dynamicMatches);

        // Act
        var result = (await _handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        // Verify mapping of the first item
        result[0].TournamentId.Should().Be(tournamentId);
        result[0].TournamentName.Should().Be("Autumn Cup");
        result[0].HomeTeamName.Should().Be("Lions");
        result[0].GuestTeamName.Should().Be("Tigers");

        _matchRepositoryMock.Verify(r => r.GetByTournamentIdAsync(tournamentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the handler returns an empty collection when no matches are found.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnEmptyCollection_WhenNoMatchesFound()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var query = new GetTournamentMatchesQuery(tournamentId);

        _matchRepositoryMock
            .Setup(r => r.GetByTournamentIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<dynamic>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    #region Helpers

    /// <summary>
    /// Helper to create an ExpandoObject that mimics the dynamic properties 
    /// returned by the repository's SQL query.
    /// </summary>
    private static dynamic CreateDynamicMatchRow(
        Guid id,
        Guid tournamentId,
        string tournamentName,
        string homeName,
        string guestName)
    {
        dynamic row = new ExpandoObject();
        row.id = id;
        row.tournamentid = tournamentId;
        row.tournamentname = tournamentName;
        row.hometeamid = Guid.NewGuid();
        row.hometeamname = homeName;
        row.guestteamid = Guid.NewGuid();
        row.guestteamname = guestName;
        row.scheduledat = DateTime.UtcNow.AddHours(2);
        row.matchnumber = "M-001";
        row.venue = "Central Stadium";
        row.temperature = 20.5;
        row.homescore = 0;
        row.guestscore = 0;
        row.createdat = DateTime.UtcNow;
        return row;
    }

    #endregion
}