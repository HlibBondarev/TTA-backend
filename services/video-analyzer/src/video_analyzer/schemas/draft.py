"""Draft output and TTA domain contract schemas."""

from __future__ import annotations

from datetime import datetime
from enum import StrEnum
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class CamelCaseModel(BaseModel):
    """Base schema configured for camelCase JSON serialization matching TTA.WebAPI."""

    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        use_enum_values=True,
    )


class TimeAnchorType(StrEnum):
    """Matches TTA.DataAccess.Enums.TimeAnchorType serialized as strings."""

    PERIOD_START = "PeriodStart"
    PERIOD_END = "PeriodEnd"
    STOPPAGE_START = "StoppageStart"
    STOPPAGE_END = "StoppageEnd"


class FoulImpact(StrEnum):
    """Matches TTA.DataAccess.Enums.FoulImpact serialized as strings."""

    NONE = "None"
    PERSONAL_FOUL = "PersonalFoul"
    DISQUALIFICATION_WITH_SUB = "DisqualificationWithSub"
    DISQUALIFICATION_NO_SUB = "DisqualificationNoSub"


class CandidateLineup(CamelCaseModel):
    """Ranked candidate lineup suggestion when cap identity cannot be auto-confirmed."""

    match_lineup_id: UUID
    cap_number: int = Field(ge=0)
    score: float = Field(ge=0.0, le=1.0)
    reason: str


class VideoMetadata(CamelCaseModel):
    """Video stream metadata and T0 timestamp alignment provenance."""

    duration_sec: float = Field(ge=0.0)
    width: int = Field(ge=0)
    height: int = Field(ge=0)
    fps: float = Field(ge=0.0)
    has_audio: bool
    video_start_utc: datetime
    video_start_utc_source: Literal["provided", "derived_safe_past", "clamped_from_future"]


class ModelMetadata(CamelCaseModel):
    """Identifies the models and providers used to produce the draft."""

    pass1_vlm_model: str
    detector_provider: str
    detector_model: str
    tracker: str
    cap_resolver: str


class DraftTimeAnchor(CamelCaseModel):
    """Detected match timeline anchor (period boundaries or stoppages)."""

    id: UUID
    period_number: int = Field(ge=1)
    type: TimeAnchorType
    video_time_sec: float = Field(ge=0.0)
    timestamp: datetime
    confidence: float = Field(ge=0.0, le=1.0)
    needs_review: bool = False
    evidence: str | None = None


class DraftEvent(CamelCaseModel):
    """Candidate TTA game event produced for Human-in-the-Loop review."""

    id: UUID
    event_definition_id: UUID
    prompt_code: str
    event_name: str
    is_positive: bool
    is_shot: bool
    period_number: int = Field(ge=1)
    video_time_sec: float = Field(ge=0.0)
    event_timestamp: datetime
    is_lead_to_goal: bool = False
    match_lineup_id: UUID | None = None
    resolved_cap_number: int | None = None
    candidate_lineup_ids: list[CandidateLineup] = Field(default_factory=list)
    actor_bbox_normalized: list[float] | None = None
    pool_zone_hint: str | None = None
    event_confidence: float = Field(ge=0.0, le=1.0)
    cap_confidence: float | None = Field(default=None, ge=0.0, le=1.0)
    needs_review: bool = True
    review_reasons: list[str] = Field(default_factory=list)
    location_x: float | None = Field(default=None, ge=0.0, le=100.0)
    location_y: float | None = Field(default=None, ge=0.0, le=100.0)
    shot_location_x: float | None = Field(default=None, ge=0.0, le=100.0)
    shot_location_y: float | None = Field(default=None, ge=0.0, le=100.0)


class DraftPlayerPresence(CamelCaseModel):
    """Closed player presence interval. Open (null) timeOut values are strictly disallowed."""

    id: UUID
    match_lineup_id: UUID
    period_number: int = Field(ge=1)
    time_in_video_sec: float = Field(ge=0.0)
    time_out_video_sec: float = Field(gt=0.0)
    time_in: datetime
    time_out: datetime
    confidence: float = Field(ge=0.0, le=1.0)
    source: str = "visual_observation"

    @model_validator(mode="after")
    def _validate_closed_interval(self) -> DraftPlayerPresence:
        if self.time_out_video_sec <= self.time_in_video_sec:
            raise ValueError("time_out_video_sec must be strictly greater than time_in_video_sec.")
        if self.time_out <= self.time_in:
            raise ValueError("time_out must be strictly greater than time_in.")
        return self


class GeminiTokenUsage(CamelCaseModel):
    """Aggregated Gemini API token counters across one or more generate_content calls."""

    call_count: int = Field(default=0, ge=0)
    prompt_tokens: int = Field(default=0, ge=0)
    candidates_tokens: int = Field(default=0, ge=0)
    thoughts_tokens: int = Field(default=0, ge=0)
    cached_content_tokens: int = Field(default=0, ge=0)
    total_tokens: int = Field(default=0, ge=0)


class GeminiCostEstimate(CamelCaseModel):
    """Estimated USD cost breakdown derived from Gemini token counters and configured pricing."""

    currency: Literal["USD"] = "USD"
    is_estimate: bool = True
    pricing_configured: bool = True
    input_cost_usd: float | None = Field(default=None, ge=0.0)
    output_cost_usd: float | None = Field(default=None, ge=0.0)
    total_cost_usd: float | None = Field(default=None, ge=0.0)
    note: str | None = None


class GeminiStageUsageDiagnostics(CamelCaseModel):
    """Per-stage Gemini model identifier, token usage, and estimated USD cost."""

    model: str | None = None
    usage: GeminiTokenUsage = Field(default_factory=GeminiTokenUsage)
    estimated_cost: GeminiCostEstimate | None = None


class GeminiUsageDiagnostics(CamelCaseModel):
    """Combined Pass 1, Pass 2, and total Gemini token usage and estimated USD cost."""

    pass1: GeminiStageUsageDiagnostics = Field(default_factory=GeminiStageUsageDiagnostics)
    pass2: GeminiStageUsageDiagnostics = Field(default_factory=GeminiStageUsageDiagnostics)
    total: GeminiTokenUsage = Field(default_factory=GeminiTokenUsage)
    estimated_cost: GeminiCostEstimate | None = None


class PipelineDiagnostics(CamelCaseModel):
    """Execution telemetry and warnings for observability and debugging."""

    pass1_chunks_processed: int = 0
    pass1_raw_candidate_count: int = 0
    deduplicated_event_count: int = 0
    pass2_windows_processed: int = 0
    resolved_actor_count: int = 0
    unresolved_actor_count: int = 0
    warnings: list[str] = Field(default_factory=list)
    timings_sec: dict[str, float] = Field(default_factory=dict)
    gemini_usage: GeminiUsageDiagnostics = Field(default_factory=GeminiUsageDiagnostics)


class VideoAnalysisDraft(CamelCaseModel):
    """Top-level structured draft payload produced by the hybrid analysis pipeline."""

    schema_version: str = "1.0"
    job_id: UUID
    match_id: UUID
    tracked_team_id: UUID
    status: Literal["completed", "failed", "cancelled"] = "completed"
    video_metadata: VideoMetadata
    model_metadata: ModelMetadata
    time_anchors: list[DraftTimeAnchor] = Field(default_factory=list)
    events: list[DraftEvent] = Field(default_factory=list)
    player_presences: list[DraftPlayerPresence] = Field(default_factory=list)
    diagnostics: PipelineDiagnostics = Field(default_factory=PipelineDiagnostics)
