using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.PlayerPresences.Commands;
using TTA.BusinessLogic.Features.PlayerPresences.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using Match = TTA.DataAccess.Models.Match;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Handlers;
/// <summary>
/// Unit tests for the <see cref="SubstitutePlayerHandler"/> class.
/// Ensures validation logic, active player state checks, atomic repository interaction, and exception mapping are correct.
/// </summary>
public class SubstitutePlayerHandlerTests
{
    private readonly Mock<IPlayerPresenceRepository> _playerPresenceRepositoryMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<SubstitutePlayerHandler>> _loggerMock;
    private readonly SubstitutePlayerHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubstitutePlayerHandlerTests"/> class.
    /// Sets up all required repository and logger mocks.
    /// </summary>
    public SubstitutePlayerHandlerTests()
    {
        _playerPresenceRepositoryMock = new Mock<IPlayerPresenceRepository>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<SubstitutePlayerHandler>>();

        _handler = new SubstitutePlayerHandler(
            _playerPresenceRepositoryMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a substitution is successfully processed atomically using client-provided ID and timestamp,
    /// and that the substitution time boundary (Outgoing.TimeOut == Incoming.TimeIn) is invariant.
    /// </summary>
    [Fact]
    public async Task Handle_Should_SubstitutePlayer_When_RequestIsValid()
    {
        // Arrange
        var command = CreateCommand();
        var match = new Match { Id = command.MatchId };

        var activeOutgoingPresence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = command.PlayerOutLineupId,
            PeriodNumber = command.PeriodNumber,
            TimeIn = command.SubstitutionTime.AddMinutes(-10),
            TimeOut = null
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _playerPresenceRepositoryMock
            .Setup(r => r.GetMatchPresenceAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPresence> { activeOutgoingPresence });

        // Variables to capture the arguments passed to the repository
        PlayerPresence? capturedOutgoing = null;
        PlayerPresence? capturedIncoming = null;

        _playerPresenceRepositoryMock
            .Setup(r => r.RecordSubstitutionAsync(It.IsAny<PlayerPresence>(), It.IsAny<PlayerPresence>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerPresence, PlayerPresence, CancellationToken>((outg, inc, ct) =>
            {
                capturedOutgoing = outg;
                capturedIncoming = inc;
            })
            .ReturnsAsync((PlayerPresence outgoing, PlayerPresence incoming, CancellationToken ct) => incoming.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.IncomingPresenceId);

        capturedOutgoing.Should().NotBeNull();
        capturedIncoming.Should().NotBeNull();

        // Verify invariants and explicit client mapping:
        capturedOutgoing!.TimeOut.Should().Be(command.SubstitutionTime);
        capturedIncoming!.Id.Should().Be(command.IncomingPresenceId);
        capturedIncoming.TimeIn.Should().Be(command.SubstitutionTime);

        // Enforce the substitution boundary invariant
        capturedOutgoing.TimeOut.Should().Be(capturedIncoming.TimeIn,
            "The time the outgoing player exits must exactly match the time the incoming player enters.");

        _playerPresenceRepositoryMock.Verify(r => r.RecordSubstitutionAsync(
            It.Is<PlayerPresence>(p => p.Id == activeOutgoingPresence.Id && p.TimeOut == command.SubstitutionTime),
            It.Is<PlayerPresence>(p => p.Id == command.IncomingPresenceId && p.MatchLineupId == command.PlayerInLineupId && p.TimeIn == command.SubstitutionTime),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when the specified match does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_MatchDoesNotExist()
    {
        // Arrange
        var command = CreateCommand();

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Match with ID {command.MatchId} was not found.");

        // Ensure no atomic substitution operation was ever attempted
        _playerPresenceRepositoryMock.Verify(r => r.RecordSubstitutionAsync(It.IsAny<PlayerPresence>(), It.IsAny<PlayerPresence>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when trying to substitute a player who is not currently active on the field.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_OutgoingPlayerIsNotActive()
    {
        // Arrange
        var command = CreateCommand();
        var match = new Match { Id = command.MatchId };

        // Outgoing player has a TimeOut already, meaning they are on the bench
        var inactiveOutgoingPresence = new PlayerPresence
        {
            Id = Guid.NewGuid(),
            MatchLineupId = command.PlayerOutLineupId,
            PeriodNumber = command.PeriodNumber,
            TimeIn = command.SubstitutionTime.AddMinutes(-20),
            TimeOut = command.SubstitutionTime.AddMinutes(-5)
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _playerPresenceRepositoryMock
            .Setup(r => r.GetMatchPresenceAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPresence> { inactiveOutgoingPresence });

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The outgoing player is not currently active on the field in this period.");

        // Ensure no atomic substitution operation was ever attempted
        _playerPresenceRepositoryMock.Verify(r => r.RecordSubstitutionAsync(It.IsAny<PlayerPresence>(), It.IsAny<PlayerPresence>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a Postgres CHECK constraint violation occurs during the atomic substitution transaction.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_DatabaseThrowsCheckConstraintViolation()
    {
        // Arrange
        var command = CreateCommand();
        var match = new Match { Id = command.MatchId };
        var activeOutgoingPresence = new PlayerPresence { Id = Guid.NewGuid(), MatchLineupId = command.PlayerOutLineupId, PeriodNumber = command.PeriodNumber, TimeOut = null };

        var pgException = new PostgresException("Check constraint failed", "ERROR", "ERROR", "23514");

        _matchRepositoryMock.Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _playerPresenceRepositoryMock.Setup(r => r.GetMatchPresenceAsync(command.MatchId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<PlayerPresence> { activeOutgoingPresence });

        _playerPresenceRepositoryMock.Setup(r => r.RecordSubstitutionAsync(It.IsAny<PlayerPresence>(), It.IsAny<PlayerPresence>(), It.IsAny<CancellationToken>())).ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>().WithMessage("Substitution time is invalid (e.g., TimeOut occurs before TimeIn).");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a Postgres FOREIGN KEY constraint violation occurs during the atomic substitution transaction.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_DatabaseThrowsForeignKeyViolation()
    {
        // Arrange
        var command = CreateCommand();
        var match = new Match { Id = command.MatchId };
        var activeOutgoingPresence = new PlayerPresence { Id = Guid.NewGuid(), MatchLineupId = command.PlayerOutLineupId, PeriodNumber = command.PeriodNumber, TimeOut = null };

        var pgException = new PostgresException("FK constraint failed", "ERROR", "ERROR", "23503");

        _matchRepositoryMock.Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _playerPresenceRepositoryMock.Setup(r => r.GetMatchPresenceAsync(command.MatchId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<PlayerPresence> { activeOutgoingPresence });

        _playerPresenceRepositoryMock.Setup(r => r.RecordSubstitutionAsync(It.IsAny<PlayerPresence>(), It.IsAny<PlayerPresence>(), It.IsAny<CancellationToken>())).ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>().WithMessage("Related record (Match Lineup) no longer exists.");
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when a custom PL/pgSQL business rule violation occurs during the atomic substitution transaction.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_DatabaseThrowsCustomBusinessRuleViolation()
    {
        // Arrange
        var command = CreateCommand();
        var match = new Match { Id = command.MatchId };
        var activeOutgoingPresence = new PlayerPresence { Id = Guid.NewGuid(), MatchLineupId = command.PlayerOutLineupId, PeriodNumber = command.PeriodNumber, TimeOut = null };
        const string customErrorMessage = "Custom business rule failed";

        var pgException = new PostgresException(customErrorMessage, "ERROR", "ERROR", "P0001");

        _matchRepositoryMock.Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _playerPresenceRepositoryMock.Setup(r => r.GetMatchPresenceAsync(command.MatchId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<PlayerPresence> { activeOutgoingPresence });

        _playerPresenceRepositoryMock.Setup(r => r.RecordSubstitutionAsync(It.IsAny<PlayerPresence>(), It.IsAny<PlayerPresence>(), It.IsAny<CancellationToken>())).ThrowsAsync(pgException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>().WithMessage(customErrorMessage);
    }

    /// <summary>
    /// Verifies that <see cref="SubstitutePlayerHandler.Handle"/> returns the existing presence identifier without executing another DB insert
    /// when an identical substitution command is retransmitted (idempotent replay scenario).
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnExistingPresenceId_When_IdenticalRequestIsReplayed()
    {
        // Arrange
        var command = CreateCommand();
        var match = new Match { Id = command.MatchId };

        var existingIncomingPresence = new PlayerPresence
        {
            Id = command.IncomingPresenceId,
            MatchLineupId = command.PlayerInLineupId,
            PeriodNumber = command.PeriodNumber,
            TimeIn = command.SubstitutionTime,
            TimeOut = null
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _playerPresenceRepositoryMock
            .Setup(r => r.GetMatchPresenceAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPresence> { existingIncomingPresence });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(command.IncomingPresenceId);

        // Ensure repository mutation was NEVER invoked on replay
        _playerPresenceRepositoryMock.Verify(r => r.RecordSubstitutionAsync(
            It.IsAny<PlayerPresence>(),
            It.IsAny<PlayerPresence>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when <see cref="SubstitutePlayerCommand.IncomingPresenceId"/>
    /// is reused with different substitution parameters.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_When_IncomingPresenceIdIsReusedWithMismatchedPayload()
    {
        // Arrange
        var command = CreateCommand();
        var match = new Match { Id = command.MatchId };

        // Existing presence has the same ID, but a different player lineup ID
        var mismatchedExistingPresence = new PlayerPresence
        {
            Id = command.IncomingPresenceId,
            MatchLineupId = Guid.NewGuid(), // Mismatched player
            PeriodNumber = command.PeriodNumber,
            TimeIn = command.SubstitutionTime,
            TimeOut = null
        };

        _matchRepositoryMock
            .Setup(r => r.GetByIdAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _playerPresenceRepositoryMock
            .Setup(r => r.GetMatchPresenceAsync(command.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPresence> { mismatchedExistingPresence });

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"IncomingPresenceId '{command.IncomingPresenceId}' has already been used with different substitution parameters.");

        _playerPresenceRepositoryMock.Verify(r => r.RecordSubstitutionAsync(
            It.IsAny<PlayerPresence>(),
            It.IsAny<PlayerPresence>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Helper method to create a valid <see cref="SubstitutePlayerCommand"/> for testing.
    /// </summary>
    /// <returns>A populated command instance containing client-generated ID and timestamp.</returns>
    private static SubstitutePlayerCommand CreateCommand()
    {
        return new SubstitutePlayerCommand(
            MatchId: Guid.NewGuid(),
            PeriodNumber: 1,
            PlayerOutLineupId: Guid.NewGuid(),
            PlayerInLineupId: Guid.NewGuid(),
            IncomingPresenceId: Guid.NewGuid(),
            SubstitutionTime: DateTime.UtcNow.AddMinutes(-5)
        );
    }
}