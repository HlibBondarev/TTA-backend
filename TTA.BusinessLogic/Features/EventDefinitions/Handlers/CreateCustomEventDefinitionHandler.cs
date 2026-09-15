using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.EventDefinitions.Commands;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.EventDefinitions.Handlers;

/// <summary>
/// Handles the creation of custom user-owned event definitions.
/// </summary>
/// <param name="eventDefinitionRepository" >The repository for event definition data access.</param>
/// <param name="logger" >The logger instance for diagnostic information.</param>
public class CreateCustomEventDefinitionHandler(
    IEventDefinitionRepository eventDefinitionRepository,
    ILogger<CreateCustomEventDefinitionHandler> logger)
    : IRequestHandler<CreateCustomEventDefinitionCommand, EventDefinitionResponse>
{
    private readonly IEventDefinitionRepository _eventDefinitionRepository = eventDefinitionRepository;
    private readonly ILogger<CreateCustomEventDefinitionHandler> _logger = logger;

    /// <summary>
    /// Handles the execution of creating a custom event definition and persisting it via repository.
    /// </summary>
    /// <param name="request">The command containing custom event definition details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The response DTO of the created event definition.</returns>
    /// < exception cref="ConflictException">Thrown when a database business rule fails (e.g. modifying system default or soft-deleted items).</exception >
    public async Task<EventDefinitionResponse> Handle(
        CreateCustomEventDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating custom event definition {Id} for Sport {SportId} by User {OwnerId}.", request.Id, request.SportId, request.OwnerId);

        var entity = new EventDefinition
        {
            Id = request.Id,
            SportId = request.SportId,
            OwnerId = request.OwnerId,
            Name = request.Name,
            ShortName = request.ShortName,
            IsPositive = request.IsPositive,
            IsSoftDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var created = await _eventDefinitionRepository.UpsertCustomAsync(entity, cancellationToken);

            if (created == null)
            {
                _logger.LogError("Failed to create custom event definition {Id}.", request.Id);
                throw new InvalidOperationException($"Failed to create custom event definition {request.Id}.");
            }

            _logger.LogInformation("Successfully created custom event definition {Id}.", created.Id);

            return new EventDefinitionResponse(
                Id: created.Id,
                SportId: created.SportId,
                Name: created.Name,
                ShortName: created.ShortName,
                IsPositive: created.IsPositive,
                IsCustom: true,
                IsEnabled: true,
                SortOrder: 0
            );
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Failed to create custom event definition {Id} due to database rule violation: {Message}", request.Id, ex.MessageText);
            throw new ConflictException(ex.MessageText, ex);
        }
    }
}