using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.BusinessLogic.Features.PlayerPresences.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;


namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Handlers;

/// <summary>
/// Unit tests for the <see cref="InitializePresenceHandler"/> class.
/// Ensures proper validation of match existence, correct delegation of bulk insert operations, 
/// and handling of database constraints.
/// </summary>
public class InitializePresenceHandlerTests
{
    private readonly Mock<IPlayerPresenceRepository> _playerPresenceRepositoryMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<InitializePresenceHandler>> _loggerMock;
    private readonly InitializePresenceHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="InitializePresenceHandlerTests"/> class.
    /// Sets up all required repository and logger mocks.
    /// </summary>
    public InitializePresenceHandlerTests()
    {
        _playerPresenceRepositoryMock = new Mock<IPlayerPresenceRepository>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<InitializePresenceHandler>>();

        _handler = new InitializePresenceHandler(
            _playerPresenceRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a valid initialization command is successfully processed 
    /// and delegates the bulk operation to the repository.
    /// </summary>
    [Fact]
    public async Task Handle_Should_InitializePresence_When_RequestIsValid()
    {
        // Arrange
        var command = new InitializePresenceCommand(Guid.NewGuid(), 1, new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });
        var match = new Match { Id = command.MatchId };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _playerPresenceRepositoryMock
            .Setup(r => r.InitializePeriodPresenceAsync(
                command.PeriodNumber,
                It.IsAny<DateTime>(),
                command.PlayerLineupIds,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        _matchRepositoryMock.Verify(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()), Times.Once);
        _playerPresenceRepositoryMock.Verify(r => r.InitializePeriodPresenceAsync(
            command.PeriodNumber,
            It.IsAny<DateTime>(),
            command.PlayerLineupIds,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when the specified match does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_MatchDoesNotExist()
    {
        // Arrange
        var command = new InitializePresenceCommand(Guid.NewGuid(), 1, new List<Guid> { Guid.NewGuid() });

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {command.MatchId} was not found.");

        // Ensure repository initialization logic is never called if match validation fails
        _playerPresenceRepositoryMock.Verify(r => r.InitializePeriodPresenceAsync(
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a Postgres FOREIGN KEY constraint violation occurs
    /// during the bulk insert, indicating invalid lineup IDs.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_DatabaseThrowsForeignKeyViolation()
    {
        // Arrange
        var command = new InitializePresenceCommand(Guid.NewGuid(), 1, new List<Guid> { Guid.NewGuid() });
        var match = new Match { Id = command.MatchId };

        // Simulating foreign key violation (SqlState 23503)
        var pgException = new PostgresException("FK constraint failed", "ERROR", "ERROR", "23503");

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _playerPresenceRepositoryMock
            .Setup(r => r.InitializePeriodPresenceAsync(
                command.PeriodNumber,
                It.IsAny<DateTime>(),
                command.PlayerLineupIds,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("One or more provided player lineup IDs do not exist in the match protocol.");
    }
}