"""Request and match-snapshot schemas accepted by the analyzer API and CLI."""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import Field, model_validator

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
    """Minimal sport metadata from public.sports."""

    id: UUID
    name: str
    short_name: str


class SportConfigurationSnapshot(CamelCaseModel):
    """Match rule profile from public.sportconfigurations."""

    id: UUID
    uses_clean_time: bool = True
    periods_count: int = Field(ge=1)
    period_duration_minutes: int = Field(ge=1)
    active_players_limit: int = Field(ge=1)
    lineup_limit: int = Field(ge=1)
    field_length: float = Field(gt=0.0)
    field_width: float = Field(gt=0.0)
    max_personal_fouls: int | None = Field(default=None, ge=1)


class TeamSnapshot(CamelCaseModel):
    """Participating team details and optional cap color cue."""

    id: UUID
    name: str
    side: Literal["Home", "Guest"]
    cap_color_hint: str | None = None


class LineupEntry(CamelCaseModel):
    """Authoritative player entry from public.matchlineups."""

    match_lineup_id: UUID
    team_id: UUID
    number: int = Field(ge=0)
    first_name: str
    last_name: str
    position_name: str
    is_goalkeeper: bool = False


class EventDefinitionSnapshot(CamelCaseModel):
    """Active TTA definition mapped bijectively between promptCode and eventDefinitionId."""

    prompt_code: str = Field(min_length=1)
    event_definition_id: UUID
    name: str
    short_name: str
    is_positive: bool
    is_shot: bool = False
    score: int = Field(default=0, ge=0)
    foul_impact: FoulImpact = FoulImpact.NONE
    penalty_duration_seconds: int | None = Field(default=None, ge=1)


class MatchSnapshot(CamelCaseModel):
    """Immutable match context snapshot passed by .NET (or loaded from JSON in CLI)."""

    sport: SportSnapshot
    sport_configuration: SportConfigurationSnapshot
    tracked_team: TeamSnapshot
    opponent_team: TeamSnapshot
    lineup: list[LineupEntry] = Field(min_length=1)
    event_definitions: list[EventDefinitionSnapshot] = Field(min_length=1)

    @model_validator(mode="after")
    def _validate_unique_event_definitions(self) -> MatchSnapshot:
        codes = [item.prompt_code for item in self.event_definitions]
        if len(codes) != len(set(codes)):
            raise ValueError("eventDefinitions must have unique promptCode values.")
        definition_ids = [item.event_definition_id for item in self.event_definitions]
        if len(definition_ids) != len(set(definition_ids)):
            raise ValueError("eventDefinitions must have unique eventDefinitionId values.")
        return self


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
    analysis_options: AnalysisOptions = Field(default_factory=AnalysisOptions)

    @model_validator(mode="after")
    def _validate_tracked_team_consistency(self) -> AnalyzeJobRequest:
        if self.tracked_team_id != self.match_snapshot.tracked_team.id:
            raise ValueError("trackedTeamId must match matchSnapshot.trackedTeam.id.")
        return self
