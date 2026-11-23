"""Tests for Pass 1 time-anchor reconciliation and normalization invariants."""

from __future__ import annotations

from pathlib import Path

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.anchor_reconciler import AnchorReconciler
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawAnchor
from video_analyzer.schemas.draft import TimeAnchorType


def test_anchor_reconciler_collapses_duplicate_period_boundaries_and_pairs_stoppages(
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=240.0, path=tmp_path / "c1.mp4")

    res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=10.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.92
            ),
            Pass1RawAnchor(
                time_sec=30.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.80
            ),
            Pass1RawAnchor(
                time_sec=60.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=75.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.90
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        time_anchors=[
            # Dangling StoppageStart with no matching StoppageEnd before PeriodEnd
            Pass1RawAnchor(
                time_sec=45.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.85
            ),
            Pass1RawAnchor(
                time_sec=110.0, period_number=1, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
        ]
    )

    reconciled, warnings = AnchorReconciler().reconcile(
        chunk_results=[(chunk0, res0), (chunk1, res1)],
        total_duration_sec=240.0,
    )

    types = [a.type for a in reconciled]
    assert types.count(TimeAnchorType.PERIOD_START) == 1
    assert types.count(TimeAnchorType.PERIOD_END) == 1
    assert types == [
        TimeAnchorType.PERIOD_START,
        TimeAnchorType.STOPPAGE_START,
        TimeAnchorType.STOPPAGE_END,
        TimeAnchorType.PERIOD_END,
    ]
    assert reconciled[0].video_time_sec == 10.0
    assert reconciled[-1].video_time_sec == 215.0
    assert any("collapsed 2 PeriodStart" in w for w in warnings)
    assert any("dropped unpaired StoppageStart" in w for w in warnings)


def test_anchor_reconciler_flags_inverted_period_bounds(tmp_path: Path) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=80.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=40.0, period_number=1, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
        ]
    )

    reconciled, warnings = AnchorReconciler().reconcile(
        chunk_results=[(chunk0, res0)],
        total_duration_sec=120.0,
    )

    assert len(reconciled) == 2
    assert all(a.needs_review is True for a in reconciled)
    assert any("was not after PeriodStart" in w for w in warnings)
