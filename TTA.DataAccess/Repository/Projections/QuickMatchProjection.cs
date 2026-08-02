namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Represents the created quick match entity along with its provisioned infrastructure identifiers.
/// </summary>
public class QuickMatchProjection
{
    /// <summary>
    /// Gets or sets the unique identifier of the created quick match.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the quick match training tournament.
    /// </summary>
    public Guid TournamentId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the Home Squad team.
    /// </summary>
    public Guid HomeTeamId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the Opponent Squad team.
    /// </summary>
    public Guid GuestTeamId { get; set; }

    /// <summary>
    /// Gets or sets the scheduled timestamp of the match.
    /// </summary>
    public DateTime ScheduledAt { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp of the match.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}