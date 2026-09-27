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
/// Unit tests for the <see cref="CreateTournamentHandler"/> class.
/// Ensures proper validation, mapping, and database exception handling during tournament creation.
/// </summary>
public class CreateTournamentHandlerTests
{
    private readonly Mock<ITournamentRepository> _repositoryMock;
    private readonly Mock<ILogger<CreateTournamentHandler>> _loggerMock;
    private readonly CreateTournamentHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTournamentHandlerTests"/> class.
    /// </summary>
    public CreateTournamentHandlerTests()
    {
        _repositoryMock = new Mock<ITournamentRepository>();
        _loggerMock = new Mock<ILogger<CreateTournamentHandler>>();
        _handler = new CreateTournamentHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler successfully creates a tournament and returns a response DTO 
    /// when valid data is provided.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Return_Response_When_Creation_Is_Successful()
    {
        // Arrange
        var ownerId = "auth0|69cf7ec5eff8f1358a0b9ae0";
        var command = CreateCommand(ownerId);

        var persistedTournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            OwnerId = ownerId
        };

        _repositoryMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(persistedTournament);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(command.Name);
        _repositoryMock.Verify(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that an <see cref="UnauthorizedAccessException"/> is thrown 
    /// when the OwnerId is empty.
    /// </summary>
    /// <param name="invalidOwnerId">The invalid owner identifier to test.</param>
    [Theory]
    [InlineData("")]
    public async Task Handle_Should_Throw_Unauthorized_When_OwnerId_Is_Missing(string invalidOwnerId)
    {
        // Arrange
        var command = CreateCommand(invalidOwnerId);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("User identification is required to create a tournament.");

        _repositoryMock.Verify(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a <see cref="ConflictException"/> is thrown when the database 
    /// returns an invalid parameter error (SQL State 22023), typically due to date ranges.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_ConflictException_On_Invalid_Date_Range()
    {
        // Arrange
        var command = CreateCommand("auth0|valid-user");
        var postgresException = new PostgresException("Invalid range", "ERROR", "ERROR", "22023");

        _repositoryMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The tournament start date must be before the end date.");
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown when the database 
    /// returns a foreign key violation (SQL State 23503).
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_On_Foreign_Key_Violation()
    {
        // Arrange
        var command = CreateCommand("auth0|valid-user");
        var postgresException = new PostgresException("FK violation", "ERROR", "ERROR", "23503");

        _repositoryMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Tournament>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("One or more related entities (City, Sport, or Configuration) do not exist.");
    }

    /// <summary>
    /// Helper method to create a valid <see cref="CreateTournamentCommand"/>.
    /// </summary>
    /// <param name="ownerId">The owner identifier for the command.</param>
    /// <returns>A populated command instance.</returns>
    private static CreateTournamentCommand CreateCommand(string ownerId)
    {
        return new CreateTournamentCommand(
            SportId: Guid.NewGuid(),
            ConfigurationId: Guid.NewGuid(),
            CityId: Guid.NewGuid(),
            OwnerId: ownerId,
            Name: "Unit Test Tournament",
            StartDate: DateTime.UtcNow.AddDays(1),
            EndDate: DateTime.UtcNow.AddDays(2)
        );
    }
}