namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Represents the created quick match entity along with its provisioned infrastructure identifiers.
/// Implemented as an immutable record for thread safety and value semantics.
/// </summary>
/// <param name="Id">The unique identifier of the created quick match.</param>
/// <param name="TournamentId">The unique identifier of the quick match training tournament.</param>
/// <param name="HomeTeamId">The unique identifier of the Home Squad team.</param>
/// <param name="GuestTeamId">The unique identifier of the Opponent Squad team.</param>
/// <param name="ScheduledAt">The scheduled timestamp of the match.</param>
/// <param name="CreatedAt">The creation timestamp of the match.</param>
public record QuickMatchProjection(
    Guid Id,
    Guid TournamentId,
    Guid HomeTeamId,
    Guid GuestTeamId,
    DateTime ScheduledAt,
    DateTime CreatedAt
);