using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents a sport discipline definition within the system.
/// </summary>
public class Sport : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier of the sport discipline.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique name of the sport discipline (e.g., "Water Polo").
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the short code or abbreviation of the sport discipline (e.g., "WP").
    /// </summary>
    public string ShortName { get; set; } = null!;

    /// <summary>
    /// Gets or sets the unique identifier of the default sport configuration associated with this sport.
    /// </summary>
    public Guid DefaultConfigId { get; set; }
}