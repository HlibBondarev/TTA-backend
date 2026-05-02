using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents the time interval a player spent on the field during a specific match period.
/// Used for calculating "Time on Ice/Field" and tracking player rotations.
/// </summary>
public class PlayerPresence : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier for the presence record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the match identifier.
    /// </summary>
    public Guid MatchId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the player's entry in the match protocol.
    /// Links this presence to a specific player in the match lineup.
    /// </summary>
    public Guid MatchLineupId { get; set; }

    /// <summary>
    /// Gets or sets the period number (e.g., 1st half, 2nd period) during which the presence occurred.
    /// </summary>
    public int PeriodNumber { get; set; }

    /// <summary>
    /// Gets or sets the absolute timestamp when the player entered the field.
    /// </summary>
    public DateTime TimeIn { get; set; }

    /// <summary>
    /// Gets or sets the absolute timestamp when the player left the field.
    /// Can be null if the player is currently on the field or the period hasn't ended.
    /// </summary>
    public DateTime? TimeOut { get; set; }
}
