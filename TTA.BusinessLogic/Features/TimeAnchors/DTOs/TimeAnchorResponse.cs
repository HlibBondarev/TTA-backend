using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.TimeAnchors.DTOs;

/// <summary>
/// Data transfer object representing a time anchor for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the time anchor.</param>
/// <param name="MatchId">The unique identifier of the associated match.</param>
/// <param name="PeriodNumber">The match period number when the anchor was created.</param>
/// <param name="Type">The specific type of the time anchor (e.g., PeriodStart).</param>
/// <param name="Timestamp">The UTC date and time when the anchor occurred.</param>
public record TimeAnchorResponse(
    Guid Id,
    Guid MatchId,
    int PeriodNumber,
    TimeAnchorType Type,
    DateTime Timestamp);