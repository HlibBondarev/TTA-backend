using MediatR;

namespace TTA.BusinessLogic.Features.GameEvents.Commands;

/// <summary>
/// Command to permanently remove a game event record.
/// </summary>
/// <param name="Id">The unique identifier of the game event to delete.</param>
public record DeleteGameEventCommand(Guid Id) : IRequest<bool>;