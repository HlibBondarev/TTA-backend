namespace TTA.DataAccess.Repository;

/// <summary>
/// Dapper parameter names (without the <c>@</c> prefix) used by the SQL statements declared in <see cref="SqlStatements"/>.
/// </summary>
public static class SqlParameters
{
    /// <summary>Parameter name for the entity identifier.</summary>
    public const string Id = "p_id";

    /// <summary>Parameter name for the user identifier.</summary>
    public const string UserId = "p_user_id";

    /// <summary>Parameter name for the match identifier.</summary>
    public const string MatchId = "p_match_id";

    /// <summary>Parameter name for the sport identifier.</summary>
    public const string SportId = "p_sport_id";

    /// <summary>Parameter name for the owner user identifier.</summary>
    public const string OwnerId = "p_owner_id";

    /// <summary>Parameter name for the event definition display name.</summary>
    public const string Name = "p_name";

    /// <summary>Parameter name for the event definition short name.</summary>
    public const string ShortName = "p_short_name";

    /// <summary>Parameter name for the positive-event flag.</summary>
    public const string IsPositive = "p_is_positive";

    /// <summary>Parameter name for the shot-event flag.</summary>
    public const string IsShot = "p_is_shot";

    /// <summary>Parameter name for the event score value.</summary>
    public const string Score = "p_score";

    /// <summary>Parameter name for the foul impact integer value (<c>FoulImpact</c> enum).</summary>
    public const string FoulImpact = "p_foulimpact";

    /// <summary>Parameter name for the penalty duration in seconds.</summary>
    public const string PenaltyDurationSeconds = "p_penaltydurationseconds";
}
