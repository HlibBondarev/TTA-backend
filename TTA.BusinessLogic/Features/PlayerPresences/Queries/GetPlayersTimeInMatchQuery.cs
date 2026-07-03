using MediatR;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;

namespace TTA.BusinessLogic.Features.PlayerPresences.Queries;

/// <summary>
/// Immutable query record used to initiate the performance analytics pipeline 
/// that aggregates clean and dirty play times for a team within a match context.
/// </summary>
/// <param name="MatchId">The unique database reference key for the target match.</param>
/// <param name="TeamId">The unique reference key for the target team.</param>
public record GetPlayersTimeInMatchQuery(Guid MatchId, Guid TeamId) : IRequest<IEnumerable<PlayerTimeInMatchResponse>>;