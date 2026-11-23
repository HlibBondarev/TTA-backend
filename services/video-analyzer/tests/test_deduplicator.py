"""Tests for Pass 1 event deduplication and promptCode-to-UUID resolution."""

from __future__ import annotations

from pathlib import Path
from uuid import UUID

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.deduplicator import EventDeduplicator
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawEvent
from video_analyzer.schemas.request import MatchSnapshot


def test_deduplicator_preserves_positive_vs_negative_goal_uuids(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk_result = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=25.0,
                prompt_code="EVT_01_G_POS",
                period_number=1,
                is_lead_to_goal=True,
                actor_bbox_normalized=[0.4, 0.4, 0.5, 0.6],
                confidence=0.92,
            ),
            Pass1RawEvent(
                time_sec=50.0,
                prompt_code="EVT_14_GC_NEG",
                period_number=1,
                is_lead_to_goal=True,
                confidence=0.88,
            ),
        ]
    )

    events, raw_count, warnings = EventDeduplicator().process(
        chunk_results=[(chunk, chunk_result)],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=120.0,
        min_confidence=0.65,
    )

    assert raw_count == 2
    assert warnings == []
    assert len(events) == 2
    # Both share name == "Goal", but resolve to distinct seeded UUIDs
    assert events[0].definition.name == "Goal"
    assert events[1].definition.name == "Goal"
    assert events[0].definition.event_definition_id == UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f57")
    assert events[0].definition.is_positive is True
    assert events[1].definition.event_definition_id == UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f72")
    assert events[1].definition.is_positive is False


def test_deduplicator_merges_overlapping_chunks_and_warns_on_unknown_code(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")

    res0 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=112.0,  # abs = 112.0s
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.78,
            ),
            Pass1RawEvent(
                time_sec=40.0,
                prompt_code="INVENTED_CODE",
                period_number=1,
                confidence=0.95,
            ),
            Pass1RawEvent(
                time_sec=60.0,
                prompt_code="EVT_03_SP_POS",
                period_number=1,
                confidence=0.40,  # below min_confidence
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=7.8,  # abs = 112.8s (within 2.5s overlap window)
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                is_lead_to_goal=True,
                actor_bbox_normalized=[0.2, 0.3, 0.3, 0.5],
                confidence=0.91,
            )
        ]
    )

    events, raw_count, warnings = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[(chunk0, res0), (chunk1, res1)],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )

    assert raw_count == 4
    assert len(warnings) == 1
    assert "INVENTED_CODE" in warnings[0]
    assert len(events) == 1
    assert events[0].confidence == 0.91
    assert events[0].video_time_sec == 112.8
    assert events[0].is_lead_to_goal is True
    assert events[0].actor_bbox_normalized == [0.2, 0.3, 0.3, 0.5]
