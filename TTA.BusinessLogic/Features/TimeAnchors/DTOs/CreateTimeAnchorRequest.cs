using TTA.BusinessLogic.Features.TimeAnchors.Commands;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.TimeAnchors.DTOs;

/// <summary>
/// Data transfer object for creating a new time anchor.
/// </summary>
/// <param name="PeriodNumber">The match period number (e.g., 1, 2, etc.).</param>
/// <param name="Type">The type of the time anchor (e.g., PeriodStart, PeriodEnd).</param>
public record CreateTimeAnchorRequest(
    int PeriodNumber,
    TimeAnchorType Type);

/// <summary>
/// Mapping extensions for <see cref="CreateTimeAnchorRequest"/>.
/// </summary>
public static class CreateTimeAnchorRequestExtensions
{
    /// <summary>
    /// Converts a request DTO to a creation command including the match context.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="matchId">The unique identifier of the match from the route.</param>
    /// <returns>A configured <see cref="CreateTimeAnchorCommand"/>.</returns>
    public static CreateTimeAnchorCommand ToCommand(this CreateTimeAnchorRequest request, Guid matchId) => new(
        MatchId: matchId,
        PeriodNumber: request.PeriodNumber,
        Type: request.Type);
}