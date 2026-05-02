using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.BusinessLogic.Features.MatchLineups.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Handlers;

/// <summary>
/// Unit tests for the <see cref="AddPlayerToMatchLineupHandler"/> ensuring correct business logic validation
/// and proper handling of database exceptions.
/// </summary>
public class AddPlayerToMatchLineupHandlerTests
{
    private readonly Mock<IMatchLineupRepository> _matchLineupRepoMock;
    private readonly Mock<ILogger<AddPlayerToMatchLineupHandler>> _loggerMock;
    private readonly AddPlayerToMatchLineupHandler _handler;

    public AddPlayerToMatchLineupHandlerTests()
    {
        _matchLineupRepoMock = new Mock<IMatchLineupRepository>();
        _loggerMock = new Mock<ILogger<AddPlayerToMatchLineupHandler>>();

        _handler = new AddPlayerToMatchLineupHandler(
            _matchLineupRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a valid request successfully returns the newly created lineup item ID.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnNewId()
    {
        // Arrange
        var command = CreateCommand();
        var expectedId = Guid.NewGuid();

        _matchLineupRepoMock
            .Setup(x => x.UpsertLineupItemAsync(It.IsAny<MatchLineup>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MatchLineup { Id = expectedId });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(expectedId);
        _matchLineupRepoMock.Verify(x => x.UpsertLineupItemAsync(
            It.Is<MatchLineup>(m => m.MatchId == command.MatchId && m.PlayerRosterId == command.PlayerRosterId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that P0001 SQL state throws a ConflictException for ineligible players.
    /// </summary>
    [Fact]
    public async Task Handle_IneligiblePlayer_ShouldThrowConflictException()
    {
        // Arrange
        var command = CreateCommand();
        var pgException = CreatePostgresException("P0001", "Player does not belong to match teams");

        _matchLineupRepoMock
            .Setup(x => x.UpsertLineupItemAsync(It.IsAny<MatchLineup>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The player does not belong to any team participating in this match.");
    }

    /// <summary>
    /// Verifies that P0003 SQL state throws a ConflictException when lineup limits are exceeded.
    /// </summary>
    [Fact]
    public async Task Handle_LineupLimitExceeded_ShouldThrowConflictException()
    {
        // Arrange
        var command = CreateCommand();
        var errorMessage = "Team lineup limit exceeded for Match";
        var pgException = CreatePostgresException("P0003", errorMessage);

        _matchLineupRepoMock
            .Setup(x => x.UpsertLineupItemAsync(It.IsAny<MatchLineup>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"*{errorMessage}*");
    }

    /// <summary>
    /// Verifies that 23505 SQL state throws a ConflictException when a player is already in the lineup.
    /// Matches the specific message from CreateMatchLineupHandler.
    /// </summary>
    [Fact]
    public async Task Handle_DuplicatePlayerInLineup_ShouldThrowConflictException()
    {
        // Arrange
        var command = CreateCommand();
        var pgException = CreatePostgresException("23505", "Unique violation");

        _matchLineupRepoMock
            .Setup(x => x.UpsertLineupItemAsync(It.IsAny<MatchLineup>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("This player is already registered in the lineup for this match.");
    }

    #region Helpers

    /// <summary>
    /// Creates a default command instance for testing.
    /// </summary>
    private static AddPlayerToMatchLineupCommand CreateCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 10, true, Guid.NewGuid());

    /// <summary>
    /// Factory method to create a <see cref="PostgresException"/> for simulation.
    /// Matches the implementation style used in AddPlayerToRosterHandlerTests.
    /// </summary>
    private static PostgresException CreatePostgresException(string sqlState, string message)
    {
        return new PostgresException(
            messageText: message,
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
    }

    #endregion
}