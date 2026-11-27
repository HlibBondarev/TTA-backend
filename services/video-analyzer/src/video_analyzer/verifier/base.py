"""Verifier interface (facilitates testing and model swaps)."""

from __future__ import annotations

import threading
from abc import ABC, abstractmethod
from collections.abc import Sequence
from pathlib import Path

from video_analyzer.pass1.models import (
    LabelConflictGroup,
    Pass1AnchorCandidate,
    Pass1EventCandidate,
)
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.verifier.models import VerificationResult


class BaseEventVerifier(ABC):
    """Re-checks Pass 1 candidates on short clips before they reach Pass 2."""

    @abstractmethod
    def verify(
        self,
        context: AnalysisContext,
        events: Sequence[Pass1EventCandidate],
        anchors: Sequence[Pass1AnchorCandidate],
        mezzanine_path: Path,
        work_dir: Path,
        conflict_groups: Sequence[LabelConflictGroup] = (),
        cancel_event: threading.Event | None = None,
    ) -> VerificationResult:
        """Verify ``events`` and ``anchors`` using clips cut from ``mezzanine_path``.

        Implementations must fail open: when verification is impossible (breaker, budget, API
        error) the candidate is kept and flagged for review instead of being lost.
        """
