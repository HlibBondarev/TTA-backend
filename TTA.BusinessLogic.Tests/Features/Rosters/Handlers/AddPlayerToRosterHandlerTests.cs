using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.Rosters.Commands;
using TTA.BusinessLogic.Features.Rosters.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Rosters.Handlers;

/// <summary>
/// Unit tests for the <see cref="AddPlayerToRosterHandler"/> ensuring correct business logic validation
/// and proper handling of database exceptions.
/// </summary>
public class AddPlayerToRosterHandlerTests
{
    private readonly Mock<IRosterRepository> _rosterRepoMock;
    private readonly Mock<ITournamentRepository> _tournamentRepoMock;
    private readonly Mock<ILogger<AddPlayerToRosterHandler>> _loggerMock;
    private readonly AddPlayerToRosterHandler _handler;

    public AddPlayerToRosterHandlerTests()
    {
        _rosterRepoMock = new Mock<IRosterRepository>();
        _tournamentRepoMock = new Mock<ITournamentRepository>();
        _loggerMock = new Mock<ILogger<AddPlayerToRosterHandler>>();

        _handler = new AddPlayerToRosterHandler(
            _rosterRepoMock.Object,
            _tournamentRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a player is successfully added to the roster when the tournament is active.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnRosterId()
    {
        // Arrange
        var command = CreateCommand();
        var tournament = new Tournament { Id = command.TournamentId, EndDate = DateTime.UtcNow.AddDays(5) };
        var rosterItem = command.ToModel();

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(command.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tournament);

        _rosterRepoMock
            .Setup(x => x.UpsertRosterItemAsync(It.IsAny<PlayerRoster>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rosterItem);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(rosterItem.Id);
        _rosterRepoMock.Verify(x => x.UpsertRosterItemAsync(It.IsAny<PlayerRoster>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when the tournament does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_TournamentNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = CreateCommand();
        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(command.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament)null!);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*ID {command.TournamentId}*");
    }

    /// <summary>
    /// Verifies that <see cref="BadRequestException"/> is thrown when the tournament has already ended.
    /// </summary>
    [Fact]
    public async Task Handle_TournamentFinished_ShouldThrowBadRequestException()
    {
        // Arrange
        var command = CreateCommand();
        var finishedTournament = new Tournament { Id = command.TournamentId, EndDate = DateTime.UtcNow.AddDays(-1) };

        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(command.TournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finishedTournament);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Cannot modify rosters for a finished tournament.");
    }

    /// <summary>
    /// Verifies that a duplicate jersey number (Postgres error 23505) results in a <see cref="ConflictException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_DuplicateJerseyNumber_ShouldThrowConflictException()
    {
        // Arrange
        var command = CreateCommand();
        SetupActiveTournament(command.TournamentId);

        var pgException = CreatePostgresException("23505", "Jersey number conflict");

        _rosterRepoMock
            .Setup(x => x.UpsertRosterItemAsync(It.IsAny<PlayerRoster>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"Jersey number {command.Number} is already taken*");
    }

    /// <summary>
    /// Verifies that a player already registered in another team (Postgres error P0001) 
    /// results in a <see cref="ConflictException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_PlayerInAnotherTeam_ShouldThrowConflictException()
    {
        // Arrange
        var command = CreateCommand();
        SetupActiveTournament(command.TournamentId);

        var pgException = CreatePostgresException("P0001", "Player is already registered for another team in this tournament.");

        _rosterRepoMock
            .Setup(x => x.UpsertRosterItemAsync(It.IsAny<PlayerRoster>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        // Added wildcard '*' to handle the "P0001: " prefix added by PostgresException
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Player is already registered for another team in this tournament.*");
    }

    #region Helpers

    /// <summary>
    /// Creates a default command instance for testing.
    /// </summary>
    private static AddPlayerToRosterCommand CreateCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7);

    /// <summary>
    /// Configures the tournament repository mock to return an active tournament.
    /// </summary>
    private void SetupActiveTournament(Guid tournamentId)
    {
        _tournamentRepoMock
            .Setup(x => x.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = tournamentId, EndDate = DateTime.UtcNow.AddDays(1) });
    }

    /// <summary>
    /// Factory method to create a <see cref="PostgresException"/> for simulation.
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