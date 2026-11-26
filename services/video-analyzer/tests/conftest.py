"""Shared pytest fixtures for video-analyzer unit and contract tests."""

from __future__ import annotations

import json
from pathlib import Path
from uuid import uuid4

import pytest

from video_analyzer.gemini.resilience import reset_shared_pacers
from video_analyzer.schemas.request import (
    AnalysisOptions,
    AnalyzeJobRequest,
    MatchSnapshot,
    VideoSource,
)

SAMPLE_SNAPSHOT_PATH = (
    Path(__file__).resolve().parents[1] / "sample_snapshots" / "waterpolo_default_snapshot.json"
)


@pytest.fixture(autouse=True)
def _isolated_shared_pacers():
    """Give every test fresh per-model RPM pacers (they are process-wide in production)."""
    reset_shared_pacers()
    yield
    reset_shared_pacers()


@pytest.fixture()
def sample_snapshot() -> MatchSnapshot:
    """Load the seeded Water Polo MatchSnapshot fixture."""
    raw = json.loads(SAMPLE_SNAPSHOT_PATH.read_text(encoding="utf-8"))
    return MatchSnapshot.model_validate(raw)


@pytest.fixture()
def sample_job_request(sample_snapshot: MatchSnapshot, tmp_path: Path) -> AnalyzeJobRequest:
    """Create a valid AnalyzeJobRequest pointing to a dummy local video file."""
    video_file = tmp_path / "match.mp4"
    video_file.write_bytes(b"dummy")
    return AnalyzeJobRequest(
        job_id=uuid4(),
        match_id=uuid4(),
        tracked_team_id=sample_snapshot.tracked_team.id,
        video_source=VideoSource(kind="local_path", local_path=str(video_file)),
        match_snapshot=sample_snapshot,
        analysis_options=AnalysisOptions(),
    )
