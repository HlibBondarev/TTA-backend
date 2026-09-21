using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.BusinessLogic.Features.EventDefinitions.Handlers;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Handlers;

/// <summary>
/// Unit tests for <see cref="CreateCustomEventDefinitionHandler" />.
/// </summary>
public class CreateCustomEventDefinitionHandlerTests
{
    private readonly Mock<IEventDefinitionRepository> _repositoryMock;
    private readonly Mock<ILogger<CreateCustomEventDefinitionHandler>> _loggerMock;
    private readonly CreateCustomEventDefinitionHandler _handler;

    public CreateCustomEventDefinitionHandlerTests()
    {
        _repositoryMock = new Mock<IEventDefinitionRepository>();
        _loggerMock = new Mock<ILogger<CreateCustomEventDefinitionHandler>>();
        _handler = new CreateCustomEventDefinitionHandler(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="CreateCustomEventDefinitionHandler.Handle" /> successfully creates and
    /// persists a custom event definition entity via repository and returns a mapped response DTO.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldCreateAndReturnEventDefinitionResponse_WhenRepositoryReturnsEntity()
    {
        // Arrange
        var command = new CreateCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            SportId: Guid.NewGuid(),
            OwnerId: "auth0|user123",
            Name: "Custom Block",
            ShortName: "C-BLK",
            IsPositive: true);

        var createdEntity = new EventDefinition
        {
            Id = command.Id,
            SportId = command.SportId,
            OwnerId = command.OwnerId,
            Name = command.Name,
            ShortName = command.ShortName,
            IsPositive = command.IsPositive,
            IsSoftDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        const int expectedSortOrder = 3;

        _repositoryMock
            .Setup(r => r.UpsertCustomAsync(It.Is<EventDefinition>(e =>
                e.Id == command.Id &&
                e.SportId == command.SportId &&
                e.OwnerId == command.OwnerId &&
                e.Name == command.Name &&
                e.ShortName == command.ShortName &&
                e.IsPositive == command.IsPositive &&
                !e.IsSoftDeleted), It.IsAny<CancellationToken>()))
            .ReturnsAsync((createdEntity, expectedSortOrder));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(command.Id);
        result.SportId.Should().Be(command.SportId);
        result.Name.Should().Be("Custom Block");
        result.ShortName.Should().Be("C-BLK");
        result.IsPositive.Should().BeTrue();
        result.IsCustom.Should().BeTrue();
        result.IsEnabled.Should().BeTrue();
        result.SortOrder.Should().Be(expectedSortOrder);

        _repositoryMock.Verify(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="CreateCustomEventDefinitionHandler.Handle" /> throws an 
    /// <see cref="InvalidOperationException" /> when repository persistence fails and returns null.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowInvalidOperationException_WhenRepositoryReturnsNull()
    {
        // Arrange
        var command = new CreateCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            SportId: Guid.NewGuid(),
            OwnerId: "auth0|user123",
            Name: "Failed Event",
            ShortName: "FAIL",
            IsPositive: false);

        _repositoryMock
            .Setup(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((EventDefinition?)null, 0));

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Failed to create custom event definition {command.Id}.");

        _repositoryMock.Verify(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="CreateCustomEventDefinitionHandler.Handle" /> correctly passes 
    /// the cancellation token to the repository method call.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPassCancellationTokenToRepository()
    {
        // Arrange
        var command = new CreateCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            SportId: Guid.NewGuid(),
            OwnerId: "auth0|user123",
            Name: "Test Event",
            ShortName: "TEST",
            IsPositive: true);

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var createdEntity = new EventDefinition
        {
            Id = command.Id,
            SportId = command.SportId,
            OwnerId = command.OwnerId,
            Name = command.Name,
            ShortName = command.ShortName,
            IsPositive = command.IsPositive,
            CreatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), cancellationToken))
            .ReturnsAsync((createdEntity, 0));

        // Act
        await _handler.Handle(command, cancellationToken);

        // Assert
        _repositoryMock.Verify(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), cancellationToken), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="CreateCustomEventDefinitionHandler.Handle" /> catches a <see cref="PostgresException" />
    /// with SQLSTATE P0001 and throws a <see cref="ConflictException" />.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenDatabaseRuleFails()
    {
        // Arrange
        var command = new CreateCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            SportId: Guid.NewGuid(),
            OwnerId: "user-123",
            Name: "Custom Action",
            ShortName: "CA",
            IsPositive: true
        );

        var postgresException = new PostgresException("Cannot update or reuse a soft-deleted event definition.", "ERROR", "ERROR", "P0001");

        _repositoryMock
            .Setup(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Cannot update or reuse a soft-deleted event definition.");
    }

    /// <summary>
    /// Verifies that <see cref="CreateCustomEventDefinitionHandler.Handle"/> succeeds when creating a custom 
    /// event definition with the same name as an existing definition for the same owner, but with an opposite IsPositive flag.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldCreateAndReturnEventDefinitionResponse_WhenOppositeIsPositiveCustomDefinitionExists()
    {
        // Arrange
        var command = new CreateCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            SportId: Guid.NewGuid(),
            OwnerId: "auth0|user123",
            Name: "Foul",
            ShortName: "FOL-P",
            IsPositive: true); // Positive "Foul" (e.g. Foul drawn)

        var createdEntity = new EventDefinition
        {
            Id = command.Id,
            SportId = command.SportId,
            OwnerId = command.OwnerId,
            Name = command.Name,
            ShortName = command.ShortName,
            IsPositive = command.IsPositive,
            IsSoftDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        const int expectedSortOrder = 4;

        _repositoryMock
            .Setup(r => r.UpsertCustomAsync(It.Is<EventDefinition>(e =>
                e.Id == command.Id &&
                e.Name == "Foul" &&
                e.IsPositive == true), It.IsAny<CancellationToken>()))
            .ReturnsAsync((createdEntity, expectedSortOrder));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(command.Id);
        result.Name.Should().Be("Foul");
        result.IsPositive.Should().BeTrue();
        result.SortOrder.Should().Be(expectedSortOrder);

        _repositoryMock.Verify(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="CreateCustomEventDefinitionHandler.Handle"/> throws <see cref="ConflictException"/>
    /// when the underlying repository throws a PostgreSQL P0001 exception due to a duplicate active name and positivity.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldThrowConflictException_WhenRepositoryThrowsP0001PostgresException()
    {
        // Arrange
        var command = new CreateCustomEventDefinitionCommand(
            Id: Guid.NewGuid(),
            SportId: Guid.NewGuid(),
            OwnerId: "auth0|test-user",
            Name: "Duplicate Action",
            ShortName: "DUP",
            IsPositive: true
        );

        var postgresException = new PostgresException(
            "An active custom event definition with this name and positivity already exists for the sport.",
            "ERROR",
            "ERROR",
            "P0001"
        );

        _repositoryMock
            .Setup(r => r.UpsertCustomAsync(It.IsAny<EventDefinition>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*An active custom event definition with this name and positivity already exists for the sport.*");
    }
}