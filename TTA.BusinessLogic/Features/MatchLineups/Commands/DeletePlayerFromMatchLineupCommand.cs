using MediatR;

namespace TTA.BusinessLogic.Features.MatchLineups.Commands;

/// <summary>
/// Command to remove a player entry from the match lineup.
/// </summary>
/// <param name="Id">The unique identifier of the match lineup record to delete.</param>
public record DeletePlayerFromMatchLineupCommand(Guid Id) : IRequest<bool>;