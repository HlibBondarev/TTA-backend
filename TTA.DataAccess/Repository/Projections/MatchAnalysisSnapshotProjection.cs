namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Aggregated database projection of everything required to build a match analysis snapshot
/// for a match and a tracked team.
/// </summary>
/// <param name="Context">The match, teams, sport and actual sport configuration data.</param>
/// <param name="Lineup">The lineup of the tracked team (may be empty).</param>
/// <param name="EventDefinitions">Active system-level event definitions of the match sport.</param>
public record MatchAnalysisSnapshotProjection(
    MatchAnalysisContextProjection Context,
    IReadOnlyList<MatchAnalysisLineupProjection> Lineup,
    IReadOnlyList<MatchAnalysisEventDefinitionProjection> EventDefinitions);

/// <summary>
/// Flat projection of the match, participating teams, sport and the actual (tournament) sport configuration.
/// </summary>
/// <param name="MatchId">The match unique identifier.</param>
/// <param name="TournamentId">The tournament unique identifier.</param>
/// <param name="TournamentName">The tournament display name.</param>
/// <param name="ScheduledAt">The scheduled match date and time.</param>
/// <param name="MatchNumber">The optional match number.</param>
/// <param name="Venue">The optional match venue.</param>
/// <param name="Temperature">The optional recorded temperature.</param>
/// <param name="HomeScore">The optional home team score.</param>
/// <param name="GuestScore">The optional guest team score.</param>
/// <param name="HomeTeamId">The home team unique identifier.</param>
/// <param name="HomeTeamName">The home team display name.</param>
/// <param name="GuestTeamId">The guest team unique identifier.</param>
/// <param name="GuestTeamName">The guest team display name.</param>
/// <param name="SportId">The sport unique identifier.</param>
/// <param name="SportName">The sport display name.</param>
/// <param name="SportShortName">The sport short code.</param>
/// <param name="ConfigurationId">The unique identifier of the tournament sport configuration.</param>
/// <param name="UsesCleanTime">Whether the match clock pauses on stoppages.</param>
/// <param name="PeriodsCount">The number of periods in the match.</param>
/// <param name="PeriodDurationMinutes">The nominal period duration in minutes.</param>
/// <param name="FieldSize">The textual field size summary.</param>
/// <param name="RosterLimit">The maximum tournament roster size.</param>
/// <param name="LineupLimit">The maximum match lineup size.</param>
/// <param name="ActivePlayersLimit">The maximum simultaneous active players per team.</param>
/// <param name="Playground">The playground SVG markup.</param>
/// <param name="ShotMap">The optional shot map SVG markup.</param>
/// <param name="FieldLength">The physical field length in meters.</param>
/// <param name="FieldWidth">The physical field width in meters.</param>
/// <param name="MaxPersonalFouls">The optional personal fouls disqualification limit.</param>
public record MatchAnalysisContextProjection(
    Guid MatchId,
    Guid TournamentId,
    string TournamentName,
    DateTime ScheduledAt,
    string? MatchNumber,
    string? Venue,
    double? Temperature,
    int? HomeScore,
    int? GuestScore,
    Guid HomeTeamId,
    string HomeTeamName,
    Guid GuestTeamId,
    string GuestTeamName,
    Guid SportId,
    string SportName,
    string SportShortName,
    Guid ConfigurationId,
    bool UsesCleanTime,
    int PeriodsCount,
    int PeriodDurationMinutes,
    string FieldSize,
    int RosterLimit,
    int LineupLimit,
    int ActivePlayersLimit,
    string Playground,
    string? ShotMap,
    decimal FieldLength,
    decimal FieldWidth,
    int? MaxPersonalFouls);

/// <summary>
/// Projection of a single tracked-team lineup entry with position details.
/// </summary>
/// <param name="Id">The match lineup entry unique identifier.</param>
/// <param name="PlayerRosterId">The tournament player roster unique identifier.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The player's jersey number for the match.</param>
/// <param name="PositionId">The position definition unique identifier.</param>
/// <param name="PositionName">The position display name.</param>
/// <param name="PositionShortName">The position short code.</param>
public record MatchAnalysisLineupProjection(
    Guid Id,
    Guid PlayerRosterId,
    string FirstName,
    string LastName,
    int Number,
    Guid PositionId,
    string PositionName,
    string PositionShortName);

/// <summary>
/// Projection of a system-level event definition used by the match analysis snapshot.
/// </summary>
/// <param name="Id">The event definition unique identifier.</param>
/// <param name="Name">The event display name.</param>
/// <param name="ShortName">The event short code.</param>
/// <param name="IsPositive">Whether the event is a positive action.</param>
/// <param name="IsShot">Whether the event is a shot.</param>
/// <param name="Score">The score value awarded by the event.</param>
/// <param name="FoulImpact">The foul impact level (<c>FoulImpact</c> enum value).</param>
/// <param name="PenaltyDurationSeconds">The optional penalty duration in seconds.</param>
public record MatchAnalysisEventDefinitionProjection(
    Guid Id,
    string Name,
    string ShortName,
    bool IsPositive,
    bool IsShot,
    int Score,
    int FoulImpact,
    int? PenaltyDurationSeconds);
