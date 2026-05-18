using TTA.DataAccess.Enums;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents a critical time reference point (anchor) within a match period.
/// These points are used to calculate "clean time" using piecewise-linear normalization.
/// </summary>
public class TimeAnchor : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier for the time anchor.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the match this anchor belongs to.
    /// </summary>
    public Guid MatchId { get; set; }

    /// <summary>
    /// Gets or sets the period number (e.g., 1, 2, 3) during which the anchor was recorded.
    /// </summary>
    public int PeriodNumber { get; set; }

    /// <summary>
    /// Gets or sets the type of the anchor.
    /// 0: Period Start, 1: Period End, 2: Stoppage Start, 3: Stoppage End.
    /// </summary>
    public TimeAnchorType Type { get; set; }

    /// <summary>
    /// Gets or sets the real-world timestamp when the anchor occurred.
    /// Stored and processed strictly in UTC.
    /// </summary>
    public DateTime Timestamp { get; set; }
}