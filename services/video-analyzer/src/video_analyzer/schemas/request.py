"""Request and match-snapshot schemas accepted by the analyzer API and CLI."""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import Field, field_validator, model_validator

from video_analyzer.schemas.draft import CamelCaseModel, FoulImpact


class VideoSource(CamelCaseModel):
    """Video input location supplied by the caller (.NET backend or local CLI)."""

    kind: Literal["local_path", "presigned_url"]
    uri: str | None = None
    local_path: str | None = None

    @model_validator(mode="after")
    def _validate_location(self) -> VideoSource:
        if self.kind == "local_path" and not self.local_path:
            raise ValueError("localPath is required when videoSource.kind is 'local_path'.")
        if self.kind == "presigned_url" and not self.uri:
            raise ValueError("uri is required when videoSource.kind is 'presigned_url'.")
        return self


class SportSnapshot(CamelCaseModel):
    """Sport metadata from public.sports."""

    id: UUID
    name: str
    short_name: str


class SportConfigurationSnapshot(CamelCaseModel):
    """Actual match rule profile resolved through the tournament sport configuration.

    ``playground`` and ``shotMap`` are raw SVG markup kept for future coordinate mapping.
    They are never injected into VLM prompts.
    """

    id: UUID
    uses_clean_time: bool = True
    periods_count: int = Field(ge=1)
    period_duration_minutes: int = Field(ge=1)
    field_size: str = ""
    roster_limit: int = Field(default=1, ge=1)
    lineup_limit: int = Field(ge=1)
    active_players_limit: int = Field(ge=1)
    field_length: float = Field(gt=0.0)
    field_width: float = Field(gt=0.0)
    max_personal_fouls: int | None = Field(default=None, ge=1)
    playground: str = ""
    shot_map: str | None = None


class MatchInfoSnapshot(CamelCaseModel):
    """Match and tournament context of the snapshot."""

    id: UUID
    tournament_id: UUID
    tournament_name: str
    scheduled_at: datetime
    match_number: str | None = None
    venue: str | None = None
    temperature: float | None = None
    home_score: int | None = None
    guest_score: int | None = None


class TeamRef(CamelCaseModel):
    """Compact team reference."""

    id: UUID
    name: str


class TeamsSnapshot(CamelCaseModel):
    """Participating teams, identifying the tracked team and its opponent."""

    home: TeamRef
    guest: TeamRef
    tracked: TeamRef
    opponent: TeamRef


class LineupEntry(CamelCaseModel):
    """Tracked-team lineup entry from public.matchlineups (``id`` is the matchLineupId)."""

    id: UUID
    player_roster_id: UUID
    first_name: str
    last_name: str
    number: int = Field(ge=0)
    position_id: UUID
    position_name: str
    position_short_name: str


_FOUL_IMPACT_BY_NUMBER: dict[int, FoulImpact] = {
    0: FoulImpact.NONE,
    1: FoulImpact.PERSONAL_FOUL,
    2: FoulImpact.DISQUALIFICATION_WITH_SUB,
    3: FoulImpact.DISQUALIFICATION_NO_SUB,
}


class EventDefinitionSnapshot(CamelCaseModel):
    """Active system-level event definition as exposed by the .NET snapshot."""

    id: UUID
    name: str
    short_name: str
    is_positive: bool
    is_shot: bool = False
    score: int = Field(default=0, ge=0)
    foul_impact: FoulImpact = FoulImpact.NONE
    penalty_duration_seconds: int | None = Field(default=None, ge=1)

    @field_validator("foul_impact", mode="before")
    @classmethod
    def _coerce_numeric_foul_impact(cls, value: object) -> object:
        """Accept the numeric ``FoulImpact`` enum value (0..3) emitted by .NET."""
        if isinstance(value, int) and not isinstance(value, bool):
            return _FOUL_IMPACT_BY_NUMBER.get(value, value)
        return value


class MatchSnapshot(CamelCaseModel):
    """Immutable database-backed match snapshot, identical to the .NET ``analysis-snapshot``.

    An empty ``lineup`` parses cleanly; it is rejected at the analysis boundary
    (:class:`AnalyzeJobRequest`) so inspection and tooling can still load such snapshots.
    """

    match: MatchInfoSnapshot
    teams: TeamsSnapshot
    sport: SportSnapshot
    sport_configuration: SportConfigurationSnapshot
    lineup: list[LineupEntry] = Field(default_factory=list)
    event_definitions: list[EventDefinitionSnapshot] = Field(min_length=1)

    @model_validator(mode="after")
    def _validate_unique_event_definitions(self) -> MatchSnapshot:
        definition_ids = [item.id for item in self.event_definitions]
        if len(definition_ids) != len(set(definition_ids)):
            raise ValueError("eventDefinitions must have unique id values.")
        return self


class RuntimeHints(CamelCaseModel):
    """Optional visual hints supplied per job or CLI run, separate from the DB snapshot."""

    tracked_cap_color_hint: str | None = None
    opponent_cap_color_hint: str | None = None


class AnalysisOptions(CamelCaseModel):
    """Tunable parameters for Pass 1 chunking and Pass 2 4K window inspection."""

    pass1_chunk_duration_sec: int = Field(default=120, ge=15, le=600)
    pass1_overlap_sec: int = Field(default=15, ge=0, le=120)
    pass1_target_height: int = Field(default=720, ge=240, le=1080)
    pass1_fps: float = Field(default=2.0, gt=0.0, le=15.0)
    pass2_window_half_sec: float = Field(default=2.5, ge=1.0, le=15.0)
    pass2_sample_fps: float = Field(default=4.0, gt=0.0, le=30.0)
    pass2_max_crops_per_track: int = Field(default=6, ge=1, le=12)
    min_event_confidence: float = Field(default=0.65, ge=0.0, le=1.0)
    min_cap_confidence: float = Field(default=0.80, ge=0.0, le=1.0)
    generate_player_presences: bool = False
    skip_pass2: bool = False

    @model_validator(mode="after")
    def _validate_overlap(self) -> AnalysisOptions:
        if self.pass1_overlap_sec >= self.pass1_chunk_duration_sec:
            raise ValueError("pass1OverlapSec must be smaller than pass1ChunkDurationSec.")
        return self


class AnalyzeJobRequest(CamelCaseModel):
    """Payload accepted by POST /v1/analysis/jobs."""

    job_id: UUID
    match_id: UUID
    tracked_team_id: UUID
    video_source: VideoSource
    video_start_utc: datetime | None = None
    callback_url: str | None = None
    match_snapshot: MatchSnapshot
    runtime_hints: RuntimeHints = Field(default_factory=RuntimeHints)
    analysis_options: AnalysisOptions = Field(default_factory=AnalysisOptions)

    @model_validator(mode="after")
    def _validate_tracked_team_consistency(self) -> AnalyzeJobRequest:
        if self.tracked_team_id != self.match_snapshot.teams.tracked.id:
            raise ValueError("trackedTeamId must match matchSnapshot.teams.tracked.id.")
        return self

    @model_validator(mode="after")
    def _validate_lineup_present(self) -> AnalyzeJobRequest:
        if not self.match_snapshot.lineup:
            raise ValueError(
                "matchSnapshot.lineup must contain at least one entry for the tracked team; "
                "analysis cannot resolve players without a lineup."
            )
        return self
