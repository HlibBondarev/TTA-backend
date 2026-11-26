"""Internal Pass 1 VLM structured output and deduplicated candidate models."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Annotated

from pydantic import BaseModel, Field
from pydantic.json_schema import SkipJsonSchema

from video_analyzer.schemas.draft import GeminiTokenUsage, TimeAnchorType
from video_analyzer.sport.events import EventDefinition


class Pass1RawEvent(BaseModel):
    """Raw candidate event emitted by Gemini relative to a single video chunk."""

    time_sec: float = Field(
        ge=0.0,
        description="Offset in seconds from the start of this video chunk when the action occurs.",
    )
    prompt_code: str = Field(
        description=(
            "Exact promptCode from the provided eventDefinitions catalog (never invent names)."
        ),
    )
    period_number: int = Field(
        ge=1,
        description="Match period number (1-indexed, e.g., 1..4 for quarters).",
    )
    is_lead_to_goal: bool = Field(
        default=False,
        description="True if this action directly leads to or is part of a scoring possession.",
    )
    actor_bbox_normalized: list[float] | None = Field(
        default=None,
        description="Approximate normalized [x1, y1, x2, y2] in [0, 1] for the acting player.",
    )
    pool_zone_hint: str | None = Field(
        default=None,
        description="Brief pool area label such as center_forward, right_wing_5m, or home_goal.",
    )
    cap_color_hint: str | None = Field(
        default=None,
        description="Observed cap color of the acting player (e.g., white, blue, red).",
    )
    confidence: float = Field(ge=0.0, le=1.0)
    evidence: str | None = Field(
        default=None,
        description="Brief visual/audio justification for why this action was detected.",
    )


class Pass1RawAnchor(BaseModel):
    """Raw time anchor candidate emitted by Gemini relative to a single video chunk."""

    time_sec: float = Field(
        ge=0.0,
        description="Offset in seconds from the start of this chunk.",
    )
    period_number: int = Field(ge=1)
    type: TimeAnchorType
    confidence: float = Field(ge=0.0, le=1.0)
    evidence: str | None = Field(
        default=None,
        description=(
            "Audio/visual evidence such as referee sprint whistle, period horn, or timeout signal."
        ),
    )


class Pass1ChunkResult(BaseModel):
    """Structured JSON response expected from Gemini for each chunk."""

    period_events: list[Pass1RawEvent] = Field(default_factory=list)
    time_anchors: list[Pass1RawAnchor] = Field(default_factory=list)
    token_usage: Annotated[GeminiTokenUsage | None, SkipJsonSchema()] = Field(
        default=None,
        exclude=True,
    )


@dataclass(slots=True)
class Pass1EventCandidate:
    """Absolute-time deduplicated Pass 1 candidate mapped to an authoritative EventDefinition."""

    video_time_sec: float
    definition: EventDefinition
    period_number: int
    is_lead_to_goal: bool
    actor_bbox_normalized: list[float] | None
    pool_zone_hint: str | None
    cap_color_hint: str | None
    confidence: float
    evidence: str | None = None
    needs_review: bool = False
    review_reasons: list[str] = field(default_factory=list)


@dataclass(slots=True)
class Pass1AnchorCandidate:
    """Absolute-time reconciled time anchor candidate."""

    video_time_sec: float
    period_number: int
    type: TimeAnchorType
    confidence: float
    needs_review: bool = False
    evidence: str | None = None
