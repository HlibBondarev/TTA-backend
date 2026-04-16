using MediatR;

namespace TTA.BusinessLogic.Features.Rosters.Commands;

/// <summary>
/// Command to remove a player from a tournament's roster.
/// </summary>
/// <param name="TournamentId">The identifier of the tournament.</param>
/// <param name="PlayerId">The identifier of the player to remove.</param>
public record RemovePlayerFromRosterCommand(Guid TournamentId, Guid TeamId, Guid PlayerId) : IRequest;