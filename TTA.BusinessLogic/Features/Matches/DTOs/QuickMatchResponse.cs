namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Represents the response payload returned after successfully creating a quick match.
/// </summary>
/// <param name="Id">Gets the unique identifier of the newly created match.</param>
/// <param name="TournamentId">Gets the unique identifier of the quick match training tournament container.</param>
/// <param name="HomeTeamId">Gets the unique identifier of the provisioned Home Squad team.</param>
/// <param name="GuestTeamId">Gets the unique identifier of the provisioned Opponent Squad team.</param>
/// <param name="ScheduledAt">Gets the scheduled timestamp of the quick match.</param>
/// <param name="CreatedAt">Gets the creation timestamp of the quick match.</param>
public record QuickMatchResponse(
    Guid Id,
    Guid TournamentId,
    Guid HomeTeamId,
    Guid GuestTeamId,
    DateTime ScheduledAt,
    DateTime CreatedAt);