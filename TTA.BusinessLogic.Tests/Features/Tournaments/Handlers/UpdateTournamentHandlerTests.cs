using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.Tournaments.Commands;
using TTA.BusinessLogic.Features.Tournaments.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Tournaments.Handlers;

/// <summary>
/// Contains unit tests for the <see cref="UpdateTournamentHandler"/> class.
/// </summary>
public class UpdateTournamentHandlerTests
{
    private readonly Mock<ITournamentRepository> _repositoryMock;
    private readonly Mock<ILogger<UpdateTournamentHandler>> _loggerMock;
    private readonly UpdateTournamentHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTournamentHandlerTests"/> class.
    /// </summary>
    public UpdateTournamentHandlerTests()
    {
        _repositoryMock = new Mock<ITournamentRepository>();
        _loggerMock = new Mock<ILogger<UpdateTournamentHandler>>();
        _handler = new UpdateTournamentHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that a tournament is successfully updated when the user is the authorized owner.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Return_Response_When_Update_Is_Successful()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var ownerId = "auth0|legit-owner";
        var command = CreateValidCommand(tournamentId, ownerId);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = tournamentId, OwnerId = ownerId });

        _repositoryMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = tournamentId, OwnerId = ownerId, Name = command.Name });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(tournamentId);
        _repositoryMock.Verify(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="ForbiddenException"/> is thrown when OwnerId is missing in the command.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_Forbidden_When_OwnerId_Is_Empty()
    {
        // Arrange
        var command = CreateValidCommand(Guid.NewGuid(), string.Empty);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        // FIX: Changed from UnauthorizedAccessException to ForbiddenException to match Handler logic
        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You do not have permission to update this tournament.");
    }

    /// <summary>
    /// Verifies that <see cref="NotFoundException"/> is thrown when the tournament does not exist in the database.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_Tournament_Does_Not_Exist()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var command = CreateValidCommand(tournamentId, "owner-id");

        _repositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Tournament not found.");
    }

    /// <summary>
    /// Verifies that <see cref="ForbiddenException"/> is thrown when the requester is not the owner.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_Forbidden_When_User_IsNot_Owner()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var command = CreateValidCommand(tournamentId, "attacker-id");

        _repositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = tournamentId, OwnerId = "original-owner-id" });

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        // FIX: Changed from UnauthorizedAccessException to ForbiddenException to match Handler logic
        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You do not have permission to update this tournament.");
    }

    /// <summary>
    /// Verifies mapping of Postgres "Invalid Parameter" (22023) exception to <see cref="ConflictException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Invalid_Date_Range()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var ownerId = "auth0|owner";
        var command = CreateValidCommand(tournamentId, ownerId);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = tournamentId, OwnerId = ownerId });

        var exception = new PostgresException("Invalid range", "ERROR", "ERROR", "22023");
        _repositoryMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The tournament start date must be before the end date.");
    }

    /// <summary>
    /// Verifies mapping of Postgres "FK Violation" (23503) exception to <see cref="NotFoundException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_On_Fk_Violation()
    {
        // Arrange
        var tournamentId = Guid.NewGuid();
        var ownerId = "auth0|owner";
        var command = CreateValidCommand(tournamentId, ownerId);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tournament { Id = tournamentId, OwnerId = ownerId });

        var exception = new PostgresException("FK error", "ERROR", "ERROR", "23503");
        _repositoryMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("One or more related entities (City, Sport, or Configuration) do not exist.");
    }

    /// <summary>
    /// Helper method to create a tournament command with provided parameters.
    /// </summary>
    /// <param name="id">The tournament ID.</param>
    /// <param name="ownerId">The owner identifier.</param>
    /// <returns>A new <see cref="CreateTournamentCommand"/>.</returns>
    private static UpdateTournamentCommand CreateValidCommand(Guid id, string ownerId)
    {
        return new UpdateTournamentCommand(
            id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ownerId, "Sample Tournament", DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2));
    }
}