using MediatR;
using TTA.BusinessLogic.Features.GameEvents.DTOs;

namespace TTA.BusinessLogic.Features.GameEvents.Queries;

/// <summary>
/// Query to retrieve detailed information about a specific game event by its ID.
/// </summary>
/// <param name="Id">The unique identifier of the game event.</param>
public record GetGameEventByIdQuery(Guid Id) : IRequest<GameEventResponse>;