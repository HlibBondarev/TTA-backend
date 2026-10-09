using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents a specific sport configuration defining game rules, roster limits, and physical field dimensions.
/// </summary>
public class SportConfiguration : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier for the sport configuration.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the associated sport entity.
    /// </summary>
    public Guid SportId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the match time calculation utilizes clean time logic (game clock pauses on stoppages).
    /// </summary>
    public bool UsesCleanTime { get; set; }

    /// <summary>
    /// Gets or sets the standard number of periods (e.g., quarters or halves) in a match.
    /// </summary>
    public int PeriodsCount { get; set; }

    /// <summary>
    /// Gets or sets the nominal duration of a single period in minutes.
    /// </summary>
    public int PeriodDurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the descriptive textual summary of the field dimensions (e.g., "25x20 sq.m.").
    /// </summary>
    public string FieldSize { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum allowed number of players registered in a tournament team roster.
    /// </summary>
    public int RosterLimit { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed number of players entered into a match protocol/lineup.
    /// </summary>
    public int LineupLimit { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of active players allowed simultaneously on the field for a single team during gameplay.
    /// </summary>
    public int ActivePlayersLimit { get; set; }

    /// <summary>
    /// Gets or sets the raw vector SVG string markup for rendering the interactive sport field layout.
    /// </summary>
    public string Playground { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exact physical length of the playing field in meters.
    /// </summary>
    public decimal FieldLength { get; set; }

    /// <summary>
    /// Gets or sets the exact physical width of the playing field in meters.
    /// </summary>
    public decimal FieldWidth { get; set; }
}