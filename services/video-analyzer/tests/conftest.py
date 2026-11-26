"""Shared pytest fixtures for video-analyzer unit and contract tests."""

from __future__ import annotations

from pathlib import Path
from uuid import uuid4

import pytest

from tests.support import (
    DEFAULT_OPPONENT_CAP_HINT,
    DEFAULT_TRACKED_CAP_HINT,
    load_sample_snapshot,
    make_context,
)
from video_analyzer.gemini.resilience import reset_shared_pacers
from video_analyzer.schemas.request import (
    AnalysisOptions,
    AnalyzeJobRequest,
    MatchSnapshot,
    RuntimeHints,
    VideoSource,
)
from video_analyzer.sport import AnalysisContext


@pytest.fixture(autouse=True)
def _isolated_shared_pacers():
    """Give every test fresh per-model RPM pacers (they are process-wide in production)."""
    reset_shared_pacers()
    yield
    reset_shared_pacers()


@pytest.fixture
def sample_snapshot() -> MatchSnapshot:
    """Load the seeded Water Polo MatchSnapshot fixture."""
    return load_sample_snapshot()


@pytest.fixture
def sample_context(sample_snapshot: MatchSnapshot) -> AnalysisContext:
    """Analysis context (water polo profile, white/blue cap hints) for the sample snapshot."""
    return make_context(sample_snapshot)


@pytest.fixture
def sample_job_request(sample_snapshot: MatchSnapshot, tmp_path: Path) -> AnalyzeJobRequest:
    """Create a valid AnalyzeJobRequest pointing to a dummy local video file."""
    video_file = tmp_path / "match.mp4"
    video_file.write_bytes(b"dummy")
    return AnalyzeJobRequest(
        job_id=uuid4(),
        match_id=uuid4(),
        tracked_team_id=sample_snapshot.teams.tracked.id,
        video_source=VideoSource(kind="local_path", local_path=str(video_file)),
        match_snapshot=sample_snapshot,
        runtime_hints=RuntimeHints(
            tracked_cap_color_hint=DEFAULT_TRACKED_CAP_HINT,
            opponent_cap_color_hint=DEFAULT_OPPONENT_CAP_HINT,
        ),
        analysis_options=AnalysisOptions(),
    )
