using TTA.DataAccess.Enums;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents a Technical and Tactical Action (TTA) event definition entity.
/// </summary>
public class EventDefinition : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier of the event definition.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the associated sport.
    /// </summary>
    public Guid SportId { get; set; }

    /// <summary>
    /// Gets or sets the owner user ID for custom definitions, or null for system defaults.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the event definition.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the short display name or code of the event definition.
    /// </summary>
    public string ShortName { get; set; } = null!;

    /// <summary>
    /// Gets or sets a value indicating whether the event action has a positive statistical impact.
    /// </summary>
    public bool IsPositive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the event type requires goal shot target location tracking.
    /// </summary>
    public bool IsShot { get; set; }

    /// <summary>
    /// Gets or sets the score value points awarded for this event action.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Gets or sets how this event affects the disciplinary state of the player (foul counting or disqualification).
    /// </summary>
    public FoulImpact FoulImpact { get; set; } = FoulImpact.None;

    /// <summary>
    /// Gets or sets the team penalty duration in seconds, or null when the penalty is not time-limited.
    /// </summary>
    public int? PenaltyDurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the default frequency rank position for system default event definitions.
    /// </summary>
    public int? DefaultPosition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the record is soft deleted.
    /// </summary>
    public bool IsSoftDeleted { get; set; }

    /// <summary>
    /// Gets or sets the creation date and time of the record.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}