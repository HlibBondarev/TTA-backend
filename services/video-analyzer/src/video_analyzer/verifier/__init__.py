"""Clip-based verification stage that re-checks Pass 1 candidates before Pass 2."""

from video_analyzer.verifier.base import BaseEventVerifier
from video_analyzer.verifier.gemini_verifier import GeminiClipVerifier
from video_analyzer.verifier.models import (
    UNVERIFIED_REASON,
    VerdictKind,
    VerificationResult,
    VerifierBatchResponse,
    VerifierVerdict,
)

__all__ = [
    "UNVERIFIED_REASON",
    "BaseEventVerifier",
    "GeminiClipVerifier",
    "VerdictKind",
    "VerificationResult",
    "VerifierBatchResponse",
    "VerifierVerdict",
]
