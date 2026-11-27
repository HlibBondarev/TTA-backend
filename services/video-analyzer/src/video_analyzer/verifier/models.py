"""Verdict models and result containers for the clip-based Pass 1 verifier."""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum

from pydantic import BaseModel, Field

from video_analyzer.pass1.models import (
    ACTING_TEAM_UNKNOWN,
    ActingTeam,
    Pass1AnchorCandidate,
    Pass1EventCandidate,
)
from video_analyzer.schemas.draft import GeminiTokenUsage

UNVERIFIED_REASON: str = "UNVERIFIED"
RELABEL_REJECTED_REASON: str = "VERIFIER_RELABEL_REJECTED"
LOW_VERIFIER_CONFIDENCE_REASON: str = "LOW_VERIFIER_CONFIDENCE"
LOW_VERIFIER_CONFIDENCE_THRESHOLD: float = 0.6


class VerdictKind(StrEnum):
    """Decision the verifier returns for one candidate clip."""

    KEEP = "keep"
    DROP = "drop"
    RELABEL = "relabel"


class VerifierVerdict(BaseModel):
    """Structured verdict for one candidate of a verification batch."""

    item_id: int = Field(description="Identifier of the clip this verdict refers to.")
    verdict: VerdictKind
    relabel_prompt_code: str | None = Field(
        default=None,
        description="Allowed alternative promptCode; only meaningful when verdict is relabel.",
    )
    acting_team: ActingTeam = Field(default=ACTING_TEAM_UNKNOWN)
    is_live_play: bool = Field(default=True)
    actor_cap_color: str | None = Field(default=None)
    confidence: float = Field(ge=0.0, le=1.0)
    reason: str | None = Field(default=None)


class VerifierBatchResponse(BaseModel):
    """Structured JSON response expected from Gemini for one verification batch."""

    verdicts: list[VerifierVerdict] = Field(default_factory=list)


@dataclass(slots=True)
class VerificationResult:
    """Verified events and anchors plus the accounting data of the verification stage."""

    events: list[Pass1EventCandidate]
    anchors: list[Pass1AnchorCandidate]
    usages: list[GeminiTokenUsage | None] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
