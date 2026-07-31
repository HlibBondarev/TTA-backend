namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Represents the response payload returned after successfully creating a quick match.
/// </summary>
public class QuickMatchResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the newly created match.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the quick match training tournament container.
    /// </summary>
    public Guid TournamentId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the provisioned Home Squad team.
    /// </summary>
    public Guid HomeTeamId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the provisioned Opponent Squad team.
    /// </summary>
    public Guid GuestTeamId { get; set; }

    /// <summary>
    /// Gets or sets the scheduled timestamp of the quick match.
    /// </summary>
    public DateTime ScheduledAt { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp of the quick match.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}