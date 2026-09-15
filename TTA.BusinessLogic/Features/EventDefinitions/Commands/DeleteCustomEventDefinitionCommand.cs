using MediatR;

namespace TTA.BusinessLogic.Features.EventDefinitions.Commands;

/// <summary>
/// Command to soft-delete a custom user-owned event definition.
/// </summary>
/// <param name="Id">The unique identifier of the custom event definition to delete.</param>
/// <param name="UserId">The unique identifier of the owner user.</param>
public record DeleteCustomEventDefinitionCommand(Guid Id, string UserId) : IRequest<bool>;