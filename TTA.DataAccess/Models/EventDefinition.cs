using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents a Technical and Tactical Action (TTA) event definition entity.
/// </summary>
public class EventDefinition : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier of the event definition.
    /// </summary >
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
    /// </summary >
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the short display name or code of the event definition.
    /// </summary >
    public string ShortName { get; set; } = null!;

    /// <summary>
    /// Gets or sets a value indicating whether the event action has a positive statistical impact.
    /// </summary>
    public bool IsPositive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the record is soft deleted.
    /// </summary>
    public bool IsSoftDeleted { get; set; }

    /// <summary>
    /// Gets or sets the creation date and time of the record.
    /// </summary >
    public DateTime CreatedAt { get; set; }
}