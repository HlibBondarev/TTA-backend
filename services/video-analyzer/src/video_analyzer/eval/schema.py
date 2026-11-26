"""Ground-truth label schema and a lenient view of a generated draft for offline scoring."""

from __future__ import annotations

from pydantic import Field

from video_analyzer.schemas.draft import CamelCaseModel, TimeAnchorType


class LabeledEvent(CamelCaseModel):
    """One ground-truth game event marked by a coach."""

    time_sec: float = Field(ge=0.0, description="Event time in video seconds.")
    prompt_code: str = Field(min_length=1, description="TTA event promptCode, e.g. EVT_01_G_POS.")
    period_number: int | None = Field(default=None, ge=1)
    cap_number: int | None = Field(
        default=None,
        ge=0,
        description="Cap number of the acting tracked-team player; null when not applicable.",
    )
    notes: str | None = None


class LabeledAnchor(CamelCaseModel):
    """One ground-truth timeline anchor (period boundary or stoppage)."""

    time_sec: float = Field(ge=0.0)
    type: TimeAnchorType
    period_number: int | None = Field(default=None, ge=1)


class GroundTruthLabels(CamelCaseModel):
    """Coach-provided labels for one clip. Real clip labels are kept out of the repository."""

    schema_version: str = "1.0"
    clip_id: str = Field(min_length=1, description="Free-form clip identifier, e.g. clip1-dev.")
    description: str | None = None
    events: list[LabeledEvent] = Field(default_factory=list)
    anchors: list[LabeledAnchor] = Field(default_factory=list)


class EvalDraftEvent(CamelCaseModel):
    """Only the draft event fields the scorer needs (unknown fields are ignored)."""

    video_time_sec: float
    prompt_code: str
    resolved_cap_number: int | None = None
    period_number: int | None = None


class EvalDraftAnchor(CamelCaseModel):
    """Only the draft anchor fields the scorer needs."""

    video_time_sec: float
    type: str
    period_number: int | None = None


class EvalDraft(CamelCaseModel):
    """Lenient view of a ``VideoAnalysisDraft`` JSON file, tolerant of older/newer versions."""

    events: list[EvalDraftEvent] = Field(default_factory=list)
    time_anchors: list[EvalDraftAnchor] = Field(default_factory=list)
