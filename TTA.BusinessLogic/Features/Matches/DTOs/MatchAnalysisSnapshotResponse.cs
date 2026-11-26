namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Sport-agnostic, database-backed snapshot of everything needed to analyze a match from the perspective of a tracked team.
/// </summary>
/// <param name="Match">The match context.</param>
/// <param name="Teams">The participating teams, identifying home, guest, tracked and opponent sides.</param>
/// <param name="Sport">The sport of the match.</param>
/// <param name="SportConfiguration">The actual sport configuration of the match tournament.</param>
/// <param name="Lineup">The lineup of the tracked team only (empty when no lineup entries exist).</param>
/// <param name="EventDefinitions">Active system-level event definitions of the match sport (custom definitions excluded).</param>
public record MatchAnalysisSnapshotResponse(
    MatchAnalysisMatchResponse Match,
    MatchAnalysisTeamsResponse Teams,
    MatchAnalysisSportResponse Sport,
    MatchAnalysisSportConfigurationResponse SportConfiguration,
    IReadOnlyList<MatchAnalysisLineupPlayerResponse> Lineup,
    IReadOnlyList<MatchAnalysisEventDefinitionResponse> EventDefinitions);

/// <summary>
/// Match context data of a match analysis snapshot.
/// </summary>
/// <param name="Id">The match unique identifier.</param>
/// <param name="TournamentId">The tournament unique identifier.</param>
/// <param name="TournamentName">The tournament display name.</param>
/// <param name="ScheduledAt">The scheduled match date and time.</param>
/// <param name="MatchNumber">The optional match number.</param>
/// <param name="Venue">The optional match venue.</param>
/// <param name="Temperature">The optional recorded temperature.</param>
/// <param name="HomeScore">The optional home team score.</param>
/// <param name="GuestScore">The optional guest team score.</param>
public record MatchAnalysisMatchResponse(
    Guid Id,
    Guid TournamentId,
    string TournamentName,
    DateTime ScheduledAt,
    string? MatchNumber,
    string? Venue,
    double? Temperature,
    int? HomeScore,
    int? GuestScore);

/// <summary>
/// Participating teams of a match analysis snapshot.
/// </summary>
/// <param name="Home">The home team.</param>
/// <param name="Guest">The guest team.</param>
/// <param name="Tracked">The tracked team the snapshot is built for.</param>
/// <param name="Opponent">The opponent of the tracked team.</param>
public record MatchAnalysisTeamsResponse(
    MatchAnalysisTeamResponse Home,
    MatchAnalysisTeamResponse Guest,
    MatchAnalysisTeamResponse Tracked,
    MatchAnalysisTeamResponse Opponent);

/// <summary>
/// Compact team reference.
/// </summary>
/// <param name="Id">The team unique identifier.</param>
/// <param name="Name">The team display name.</param>
public record MatchAnalysisTeamResponse(Guid Id, string Name);

/// <summary>
/// Sport details of a match analysis snapshot.
/// </summary>
/// <param name="Id">The sport unique identifier.</param>
/// <param name="Name">The sport display name.</param>
/// <param name="ShortName">The sport short code.</param>
public record MatchAnalysisSportResponse(Guid Id, string Name, string ShortName);

/// <summary>
/// Actual sport configuration (resolved via the tournament configuration, not the sport default).
/// </summary>
/// <param name="Id">The sport configuration unique identifier.</param>
/// <param name="UsesCleanTime">Whether the match clock pauses on stoppages.</param>
/// <param name="PeriodsCount">The number of periods in the match.</param>
/// <param name="PeriodDurationMinutes">The nominal period duration in minutes.</param>
/// <param name="FieldSize">The textual field size summary.</param>
/// <param name="RosterLimit">The maximum tournament roster size.</param>
/// <param name="LineupLimit">The maximum match lineup size.</param>
/// <param name="ActivePlayersLimit">The maximum simultaneous active players per team.</param>
/// <param name="FieldLength">The physical field length in meters.</param>
/// <param name="FieldWidth">The physical field width in meters.</param>
/// <param name="MaxPersonalFouls">The optional personal fouls disqualification limit.</param>
/// <param name="Playground">The playground SVG markup for spatial coordinate mapping.</param>
/// <param name="ShotMap">The optional shot map SVG markup for shot coordinate mapping.</param>
public record MatchAnalysisSportConfigurationResponse(
    Guid Id,
    bool UsesCleanTime,
    int PeriodsCount,
    int PeriodDurationMinutes,
    string FieldSize,
    int RosterLimit,
    int LineupLimit,
    int ActivePlayersLimit,
    decimal FieldLength,
    decimal FieldWidth,
    int? MaxPersonalFouls,
    string Playground,
    string? ShotMap);

/// <summary>
/// A tracked-team lineup entry with position details.
/// </summary>
/// <param name="Id">The match lineup entry unique identifier.</param>
/// <param name="PlayerRosterId">The tournament player roster unique identifier.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="Number">The player's jersey number for the match.</param>
/// <param name="PositionId">The position definition unique identifier.</param>
/// <param name="PositionName">The position display name.</param>
/// <param name="PositionShortName">The position short code.</param>
public record MatchAnalysisLineupPlayerResponse(
    Guid Id,
    Guid PlayerRosterId,
    string FirstName,
    string LastName,
    int Number,
    Guid PositionId,
    string PositionName,
    string PositionShortName);

/// <summary>
/// A system-level event definition of the match sport.
/// </summary>
/// <param name="Id">The event definition unique identifier.</param>
/// <param name="Name">The event display name.</param>
/// <param name="ShortName">The event short code.</param>
/// <param name="IsPositive">Whether the event is a positive action.</param>
/// <param name="IsShot">Whether the event is a shot.</param>
/// <param name="Score">The score value awarded by the event.</param>
/// <param name="FoulImpact">The foul impact level.</param>
/// <param name="PenaltyDurationSeconds">The optional penalty duration in seconds.</param>
public record MatchAnalysisEventDefinitionResponse(
    Guid Id,
    string Name,
    string ShortName,
    bool IsPositive,
    bool IsShot,
    int Score,
    int FoulImpact,
    int? PenaltyDurationSeconds);
