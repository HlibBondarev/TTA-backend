using MediatR;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;

namespace TTA.BusinessLogic.Features.EventDefinitions.Commands;

///< summary>
/// Command to create a custom user-owned event definition.
/// </summary>
/// <param name="Id">The client-assigned unique identifier of the custom event definition.</param>
/// <param name="SportId">The unique identifier of the target sport.</param>
/// <param name="OwnerId">The unique identifier of the owner user.</param>
/// <param name="Name">The display name of the custom event definition.</param>
/// <param name="ShortName">The abbreviated short code of the custom event definition.</param>
/// <param name="IsPositive">Indicates whether the custom action has a positive statistical impact.</param>
public record CreateCustomEventDefinitionCommand(
    Guid Id,
    Guid SportId,
    string OwnerId,
    string Name,
    string ShortName,
    bool IsPositive) : IRequest<EventDefinitionResponse>;

/// <summary>
/// Extension methods for mapping CreateCustomEventDefinitionRequest DTO to CreateCustomEventDefinitionCommand.
/// </summary >
public static class CreateCustomEventDefinitionCommandExtensions
{
    /// <summary>
    /// Maps CreateCustomEventDefinitionRequest DTO to CreateCustomEventDefinitionCommand.
    /// </summary >
    /// <param name="request">The request DTO.</param>
    /// <param name="sportId">The sport ID.</param>
    /// <param name="ownerId">The owner user ID.</param>
    /// <returns>The mapped command.</returns>
    public static CreateCustomEventDefinitionCommand ToCommand(
        this CreateCustomEventDefinitionRequest request,
        Guid sportId,
        string ownerId)
    {
        return new CreateCustomEventDefinitionCommand(
            Id: request.Id,
            SportId: sportId,
            OwnerId: ownerId,
            Name: request.Name,
            ShortName: request.ShortName,
            IsPositive: request.IsPositive);
    }
}