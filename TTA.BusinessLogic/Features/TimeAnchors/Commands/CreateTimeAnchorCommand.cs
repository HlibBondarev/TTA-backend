using MediatR;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.TimeAnchors.Commands;

/// <summary>
/// Command to create a new time anchor for a match timeline.
/// The timestamp is generated automatically on the server side using UTC.
/// </summary>
/// <param name="MatchId">The unique identifier of the match.</param>
/// <param name="PeriodNumber">The match period number (e.g., 1, 2, etc.).</param>
/// <param name="Type">The type of the time anchor (e.g., PeriodStart, PeriodEnd).</param>
public record CreateTimeAnchorCommand(
    Guid MatchId,
    int PeriodNumber,
    TimeAnchorType Type) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping CreateTimeAnchorCommand to domain models.
/// </summary>
public static class CreateTimeAnchorCommandExtensions
{
    /// <summary>
    /// Maps the creation command to a TimeAnchor entity and sets the current UTC timestamp.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <returns>A new TimeAnchor entity with a server-generated UTC timestamp.</returns>
    public static TimeAnchor ToModel(this CreateTimeAnchorCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = cmd.MatchId,
        PeriodNumber = cmd.PeriodNumber,
        Type = cmd.Type,
        // Enforcing UTC consistency as per project standards
        Timestamp = DateTime.UtcNow
    };
}