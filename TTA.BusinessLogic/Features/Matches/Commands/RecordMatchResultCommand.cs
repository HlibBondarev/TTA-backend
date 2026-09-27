using MediatR;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Command to record the results and conditions of a match.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="HomeScore">The score of the home team.</param>
/// <param name="GuestScore">The score of the guest team.</param>
/// <param name="Temperature">The ambient temperature during the match.</param>
public record RecordMatchResultCommand(
    Guid MatchId,
    int HomeScore,
    int GuestScore,
    double? Temperature) : IRequest<Guid>;

/// <summary>
/// Mapping extensions for <see cref="RecordMatchResultCommand"/>.
/// </summary>
public static class RecordMatchResultCommandExtensions
{
    /// <summary>
    /// Maps the command to an existing <see cref="Match"/> domain model.
    /// </summary>
    public static Match SetToModel(this RecordMatchResultCommand cmd, Match match)
    {
        match.HomeScore = cmd.HomeScore;
        match.GuestScore = cmd.GuestScore;
        match.Temperature = cmd.Temperature;
        return match;
    }
}