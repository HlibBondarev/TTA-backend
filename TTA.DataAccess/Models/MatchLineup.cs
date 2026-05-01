using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents a specific entry in the match protocol.
/// Links a player from the tournament roster to a specific match.
/// </summary>
public class MatchLineup : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier for the lineup entry.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the match identifier.
    /// </summary>
    public Guid MatchId { get; set; }

    /// <summary>
    /// Gets or sets the player roster identifier (link to tournament squad).
    /// </summary>
    public Guid PlayerRosterId { get; set; }

    /// <summary>
    /// Gets or sets the jersey number for this specific match.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the player is in the starting lineup.
    /// </summary>
    public bool IsInStartingLineup { get; set; }

    /// <summary>
    /// Gets or sets the position identifier for the player in this match.
    /// </summary>
    public Guid PositionId { get; set; }
}