"""Distinct Pass 2 failure/review reasons (replaces the blanket NO_PLAYER_TRACKLET_MATCHED)."""

from __future__ import annotations

from enum import StrEnum

from video_analyzer.gemini.resilience import BreakerReason


class Pass2FailureReason(StrEnum):
    """Why Pass 2 could not produce a cap reading for an event."""

    NO_FRAMES_EXTRACTED = "NO_FRAMES_EXTRACTED"
    NO_PLAYER_TRACKLET_MATCHED = "NO_PLAYER_TRACKLET_MATCHED"
    NO_TRACKED_TEAM_ACTOR = "NO_TRACKED_TEAM_ACTOR"
    NO_USABLE_CAP_CROPS = "NO_USABLE_CAP_CROPS"
    CAP_RESOLUTION_API_ERROR = "CAP_RESOLUTION_API_ERROR"
    CAP_RESOLUTION_QUOTA_EXHAUSTED = "CAP_RESOLUTION_QUOTA_EXHAUSTED"
    CAP_RESOLUTION_BUDGET_EXCEEDED = "CAP_RESOLUTION_BUDGET_EXCEEDED"


REVIEW_WEAK_ACTOR_MATCH = "WEAK_ACTOR_MATCH"
REVIEW_ACTOR_TEAM_UNVERIFIED = "ACTOR_TEAM_UNVERIFIED"
REVIEW_OPPONENT_GOALKEEPER_SIDE = "OPPONENT_GOALKEEPER_SIDE"
REVIEW_OCR_CAP_COLOR_MISMATCH = "OCR_CAP_COLOR_MISMATCH"
REVIEW_GOALKEEPER_ACTOR_ON_FIELD_ONLY_EVENT = "GOALKEEPER_ACTOR_ON_FIELD_ONLY_EVENT"
"""Free-form ``reviewReasons`` added by Pass 2 auto-confirm gates (no contract change)."""


_BREAKER_TO_FAILURE: dict[BreakerReason, Pass2FailureReason] = {
    BreakerReason.QUOTA_EXHAUSTED: Pass2FailureReason.CAP_RESOLUTION_QUOTA_EXHAUSTED,
    BreakerReason.BUDGET_EXCEEDED: Pass2FailureReason.CAP_RESOLUTION_BUDGET_EXCEEDED,
    BreakerReason.API_ERROR: Pass2FailureReason.CAP_RESOLUTION_API_ERROR,
}


def failure_reason_for_breaker(reason: BreakerReason) -> Pass2FailureReason:
    """Map an open circuit-breaker reason to the review reason attached to unresolved events."""
    return _BREAKER_TO_FAILURE[reason]
