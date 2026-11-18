namespace TTA.DataAccess.Enums;

/// <summary>
/// Describes how an event definition affects the disciplinary state of the player who committed it.
/// Persisted as an integer in the <c>eventdefinitions.foulimpact</c> column (valid range 0 to 3).
/// </summary>
public enum FoulImpact
{
    /// <summary>
    /// Not a foul, a non-countable foul (e.g., offensive foul, ball under), or a positive action
    /// (e.g., exclusion earned, penalty earned).
    /// </summary>
    None = 0,

    /// <summary>
    /// A countable personal foul. Adds one toward <c>sportconfigurations.maxpersonalfouls</c>;
    /// the player is disqualified for the rest of the match upon reaching the threshold.
    /// </summary>
    PersonalFoul = 1,

    /// <summary>
    /// Immediate player disqualification for the rest of the match, with substitution allowed
    /// after the team penalty expires.
    /// </summary>
    DisqualificationWithSub = 2,

    /// <summary>
    /// Immediate player disqualification without substitution. The team plays shorthanded until
    /// <c>penaltydurationseconds</c> expires if it is set, or until the end of the match if it is <c>NULL</c>.
    /// </summary>
    DisqualificationNoSub = 3
}
