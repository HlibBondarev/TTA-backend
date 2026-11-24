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


def test_anchor_reconciler_ignores_out_of_range_chunk_offsets(tmp_path: Path) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=5.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=150.0,  # exceeds chunk duration 120.0s
                period_number=1,
                type=TimeAnchorType.PERIOD_END,
                confidence=0.95,
            ),
        ]
    )

    reconciled, warnings = AnchorReconciler().reconcile(
        chunk_results=[(chunk0, res0)],
        total_duration_sec=240.0,
    )

    assert len(reconciled) == 1
    assert reconciled[0].type == TimeAnchorType.PERIOD_START
    assert any("out-of-range" in w for w in warnings)


def test_anchor_reconciler_preserves_distinct_stoppages_and_avoids_cross_pairing(
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")

    # Two distinct close stoppages ([60..61] and [62..63]) in chunk0, overlap stoppage [110..112]
    # observed in both chunks, plus a true cross-chunk stoppage (Start 118 in c0 -> End 130 in c1)
    res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=5.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=60.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=61.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=62.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=63.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=110.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.80
            ),
            Pass1RawAnchor(
                time_sec=112.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.80
            ),
            Pass1RawAnchor(
                time_sec=118.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.90
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=5.4,  # abs = 110.4s (higher-conf observation of [110..112])
                period_number=1,
                type=TimeAnchorType.STOPPAGE_START,
                confidence=0.95,
            ),
            Pass1RawAnchor(
                time_sec=7.3,  # abs = 112.3s
                period_number=1,
                type=TimeAnchorType.STOPPAGE_END,
                confidence=0.95,
            ),
            Pass1RawAnchor(
                time_sec=25.0,  # abs = 130.0s (pairs with chunk0's 118.0s start)
                period_number=1,
                type=TimeAnchorType.STOPPAGE_END,
                confidence=0.92,
            ),
            Pass1RawAnchor(
                time_sec=100.0,  # abs = 205.0s
                period_number=1,
                type=TimeAnchorType.PERIOD_END,
                confidence=0.95,
            ),
        ]
    )

    reconciled, warnings = AnchorReconciler(merge_window_sec=3.0).reconcile(
        chunk_results=[(chunk0, res0), (chunk1, res1)],
        total_duration_sec=240.0,
    )

    assert warnings == []
    stoppages = [
        (a.type, a.video_time_sec)
        for a in reconciled
        if a.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END)
    ]
    assert stoppages == [
        (TimeAnchorType.STOPPAGE_START, 60.0),
        (TimeAnchorType.STOPPAGE_END, 61.0),
        (TimeAnchorType.STOPPAGE_START, 62.0),
        (TimeAnchorType.STOPPAGE_END, 63.0),
        (TimeAnchorType.STOPPAGE_START, 110.4),
        (TimeAnchorType.STOPPAGE_END, 112.3),
        (TimeAnchorType.STOPPAGE_START, 118.0),
        (TimeAnchorType.STOPPAGE_END, 130.0),
    ]


def test_anchor_reconciler_flags_overlapping_periods(tmp_path: Path) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=200.0, path=tmp_path / "c0.mp4")
    res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=10.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=120.0, period_number=1, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=110.0, period_number=2, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=190.0, period_number=2, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
        ]
    )

    reconciled, warnings = AnchorReconciler().reconcile(
        chunk_results=[(chunk0, res0)],
        total_duration_sec=200.0,
    )

    assert any("precedes Period 1" in w for w in warnings)
    p1_end = next(
        a for a in reconciled if a.period_number == 1 and a.type == TimeAnchorType.PERIOD_END
    )
    p2_start = next(
        a for a in reconciled if a.period_number == 2 and a.type == TimeAnchorType.PERIOD_START
    )
    p2_end = next(
        a for a in reconciled if a.period_number == 2 and a.type == TimeAnchorType.PERIOD_END
    )
    assert p1_end.needs_review is True
    assert p2_start.needs_review is True
    assert p2_end.needs_review is False


def test_anchor_reconciler_preserves_cross_chunk_unpaired_stoppage_pairs(
    tmp_path: Path,
) -> None:
    # Case 1: Unpaired endpoints across three overlapping chunks
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=30.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=8.0, end_sec=20.0, path=tmp_path / "c1.mp4")
    chunk2 = VideoChunk(index=2, start_sec=10.0, end_sec=40.0, path=tmp_path / "c2.mp4")

    res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=2.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=10.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.80
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=3.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=4.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.95
            ),
        ]
    )
    res2 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=3.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.80
            ),
            Pass1RawAnchor(
                time_sec=30.0, period_number=1, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
        ]
    )

    reconciled, warnings = AnchorReconciler(merge_window_sec=3.0).reconcile(
        chunk_results=[(chunk0, res0), (chunk1, res1), (chunk2, res2)],
        total_duration_sec=50.0,
    )
    assert warnings == []
    stoppages = [
        (a.type, a.video_time_sec)
        for a in reconciled
        if a.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END)
    ]
    assert stoppages == [
        (TimeAnchorType.STOPPAGE_START, 10.0),
        (TimeAnchorType.STOPPAGE_END, 11.0),
        (TimeAnchorType.STOPPAGE_START, 12.0),
        (TimeAnchorType.STOPPAGE_END, 13.0),
    ]

    # Case 2: Mixed unpaired cross-chunk stoppage [10..11] plus within-chunk stoppage [12..13]
    mixed_chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=30.0, path=tmp_path / "mc0.mp4")
    mixed_chunk1 = VideoChunk(index=1, start_sec=8.0, end_sec=40.0, path=tmp_path / "mc1.mp4")
    mixed_res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=2.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=10.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.80
            ),
        ]
    )
    mixed_res1 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=3.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=4.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=5.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=30.0, period_number=1, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
        ]
    )
    mixed_reconciled, mixed_warnings = AnchorReconciler(merge_window_sec=3.0).reconcile(
        chunk_results=[(mixed_chunk0, mixed_res0), (mixed_chunk1, mixed_res1)],
        total_duration_sec=50.0,
    )
    assert mixed_warnings == []
    mixed_stoppages = [
        (a.type, a.video_time_sec)
        for a in mixed_reconciled
        if a.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END)
    ]
    assert mixed_stoppages == [
        (TimeAnchorType.STOPPAGE_START, 10.0),
        (TimeAnchorType.STOPPAGE_END, 11.0),
        (TimeAnchorType.STOPPAGE_START, 12.0),
        (TimeAnchorType.STOPPAGE_END, 13.0),
    ]


def test_anchor_reconciler_preserves_split_stoppage_when_chunk_misses_middle_end(
    tmp_path: Path,
) -> None:
    # Case 1: Chunk 0 has starts at 10 and 12 plus end at 13; overlapping chunk 1 has end at 11.
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=30.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=5.0, end_sec=20.0, path=tmp_path / "c1.mp4")

    res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=2.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=10.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=12.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=13.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=28.0, period_number=1, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=6.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.92
            ),
        ]
    )

    reconciled, warnings = AnchorReconciler(merge_window_sec=3.0).reconcile(
        chunk_results=[(chunk0, res0), (chunk1, res1)],
        total_duration_sec=40.0,
    )
    assert warnings == []
    stoppages = [
        (a.type, a.video_time_sec)
        for a in reconciled
        if a.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END)
    ]
    assert stoppages == [
        (TimeAnchorType.STOPPAGE_START, 10.0),
        (TimeAnchorType.STOPPAGE_END, 11.0),
        (TimeAnchorType.STOPPAGE_START, 12.0),
        (TimeAnchorType.STOPPAGE_END, 13.0),
    ]

    # Case 2: Chunk 0 only sees outer [10, 13] while chunk 1 sees inner [11 (end), 12 (start)].
    split_chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=30.0, path=tmp_path / "sc0.mp4")
    split_chunk1 = VideoChunk(index=1, start_sec=5.0, end_sec=20.0, path=tmp_path / "sc1.mp4")
    split_res0 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=2.0, period_number=1, type=TimeAnchorType.PERIOD_START, confidence=0.95
            ),
            Pass1RawAnchor(
                time_sec=10.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=13.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.90
            ),
            Pass1RawAnchor(
                time_sec=28.0, period_number=1, type=TimeAnchorType.PERIOD_END, confidence=0.95
            ),
        ]
    )
    split_res1 = Pass1ChunkResult(
        time_anchors=[
            Pass1RawAnchor(
                time_sec=6.0, period_number=1, type=TimeAnchorType.STOPPAGE_END, confidence=0.92
            ),
            Pass1RawAnchor(
                time_sec=7.0, period_number=1, type=TimeAnchorType.STOPPAGE_START, confidence=0.92
            ),
        ]
    )

    split_reconciled, split_warnings = AnchorReconciler(merge_window_sec=3.0).reconcile(
        chunk_results=[(split_chunk0, split_res0), (split_chunk1, split_res1)],
        total_duration_sec=40.0,
    )
    assert split_warnings == []
    split_stoppages = [
        (a.type, a.video_time_sec)
        for a in split_reconciled
        if a.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END)
    ]
    assert split_stoppages == [
        (TimeAnchorType.STOPPAGE_START, 10.0),
        (TimeAnchorType.STOPPAGE_END, 11.0),
        (TimeAnchorType.STOPPAGE_START, 12.0),
        (TimeAnchorType.STOPPAGE_END, 13.0),
    ]
