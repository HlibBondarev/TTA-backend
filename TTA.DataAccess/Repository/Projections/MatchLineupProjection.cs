namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Projection representing a player entry in a match lineup protocol with joined metadata.
/// Implemented as an immutable record for thread safety and value semantics.
/// </summary>
/// <param name="Id">The lineup entry unique identifier.</param>
/// <param name="MatchId">The match unique identifier.</param>
/// <param name="TeamId">The team unique identifier.</param>
/// <param name="PlayerRosterId">The tournament player roster unique identifier.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The player's jersey number for the match.</param>
/// <param name="PositionId">The position definition unique identifier.</param>
/// <param name="PositionName">The human-readable position name.</param>
public record MatchLineupProjection(
    Guid Id,
    Guid MatchId,
    Guid TeamId,
    Guid PlayerRosterId,
    string FirstName,
    string LastName,
    int Number,
    Guid PositionId,
    string PositionName
);