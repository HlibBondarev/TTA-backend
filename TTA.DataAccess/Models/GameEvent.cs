using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents an event that occurred during a match.
/// </summary>
public class GameEvent : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the match lineup identifier (link to protocol).
    /// </summary>
    public Guid MatchLineupId { get; set; }

    /// <summary>
    /// Gets or sets the event definition identifier.
    /// </summary>
    public Guid EventDefinitionId { get; set; }

    /// <summary>
    /// Gets or sets the period number when the event occurred.
    /// </summary>
    public int PeriodNumber { get; set; }

    /// <summary>
    /// Gets or sets the absolute timestamp of the event.
    /// </summary>
    public DateTime EventTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the normalized match time (duration from start of the match).
    /// </summary>
    public TimeSpan? NormalizedMatchTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this event leads to a goal (e.g., assist).
    /// </summary>
    public bool IsLeadToGoal { get; set; }

    /// <summary>
    /// Gets or sets the optional X coordinate of the event on the field (0.00 to 100.00 percentage scale).
    /// </summary>
    public decimal? LocationX { get; set; }

    /// <summary>
    /// Gets or sets the optional Y coordinate of the event on the field (0.00 to 100.00 percentage scale).
    /// </summary>
    public decimal? LocationY { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
