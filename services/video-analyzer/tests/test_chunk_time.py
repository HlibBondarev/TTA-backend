"""Tests for normalizing model offsets that were reported on the global video timeline."""

from __future__ import annotations

from pathlib import Path

from tests.support import CODE_GOAL
from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.chunk_time import normalize_chunk_result
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawAnchor, Pass1RawEvent
from video_analyzer.schemas.draft import TimeAnchorType


def _chunk(tmp_path: Path, start: float, end: float) -> VideoChunk:
    return VideoChunk(index=1, start_sec=start, end_sec=end, path=tmp_path / "c.mp4")


def _event(time_sec: float) -> Pass1RawEvent:
    return Pass1RawEvent(time_sec=time_sec, prompt_code=CODE_GOAL, period_number=1, confidence=0.9)


def _anchor(time_sec: float) -> Pass1RawAnchor:
    return Pass1RawAnchor(
        time_sec=time_sec,
        period_number=1,
        type=TimeAnchorType.STOPPAGE_START,
        confidence=0.9,
    )


def test_all_global_offsets_are_shifted_by_the_chunk_start(tmp_path: Path) -> None:
    chunk = _chunk(tmp_path, 120.0, 240.24)  # duration 120.24
    result = Pass1ChunkResult(
        period_events=[_event(130.0), _event(150.5)], time_anchors=[_anchor(200.0)]
    )

    fixed, warnings = normalize_chunk_result(chunk, result)

    assert [e.time_sec for e in fixed.period_events] == [10.0, 30.5]
    assert [a.time_sec for a in fixed.time_anchors] == [80.0]
    assert len(warnings) == 1
    assert "Chunk 1" in warnings[0]
    # The original result is not mutated.
    assert [e.time_sec for e in result.period_events] == [130.0, 150.5]


def test_all_relative_offsets_are_left_untouched(tmp_path: Path) -> None:
    chunk = _chunk(tmp_path, 120.0, 240.24)
    result = Pass1ChunkResult(
        period_events=[_event(10.0), _event(100.0)], time_anchors=[_anchor(5.0)]
    )

    fixed, warnings = normalize_chunk_result(chunk, result)

    assert fixed is result
    assert warnings == []


def test_mixed_offsets_outside_the_global_window_are_not_shifted(tmp_path: Path) -> None:
    chunk = _chunk(tmp_path, 120.0, 240.0)
    # 10.0 is not inside [120, 240]: the chunk is ambiguous, so downstream drops handle 130.0.
    result = Pass1ChunkResult(period_events=[_event(10.0), _event(130.0)])

    fixed, warnings = normalize_chunk_result(chunk, result)

    assert [e.time_sec for e in fixed.period_events] == [10.0, 130.0]
    assert warnings == []


def test_first_chunk_with_zero_start_is_never_shifted(tmp_path: Path) -> None:
    chunk = _chunk(tmp_path, 0.0, 120.0)
    result = Pass1ChunkResult(period_events=[_event(119.0), _event(125.0)])

    fixed, warnings = normalize_chunk_result(chunk, result)

    assert fixed is result
    assert warnings == []


def test_overlap_ambiguity_defaults_to_relative_when_nothing_exceeds_duration(
    tmp_path: Path,
) -> None:
    chunk = _chunk(tmp_path, 105.0, 225.0)  # duration 120; offsets 110..118 fit both readings
    result = Pass1ChunkResult(period_events=[_event(110.0), _event(118.0)])

    fixed, warnings = normalize_chunk_result(chunk, result)

    assert [e.time_sec for e in fixed.period_events] == [110.0, 118.0]
    assert warnings == []


def test_empty_chunk_result_is_returned_unchanged(tmp_path: Path) -> None:
    result = Pass1ChunkResult()

    fixed, warnings = normalize_chunk_result(_chunk(tmp_path, 105.0, 225.0), result)

    assert fixed is result
    assert warnings == []


def test_normalization_preserves_token_usage_and_chunk_level_fields(tmp_path: Path) -> None:
    from video_analyzer.schemas.draft import GeminiTokenUsage

    usage = GeminiTokenUsage(call_count=1, prompt_tokens=10, total_tokens=10)
    result = Pass1ChunkResult(
        tracked_attack_direction="left",
        period_events=[_event(130.0), _event(121.0)],
        token_usage=usage,
    )

    fixed, _ = normalize_chunk_result(_chunk(tmp_path, 120.0, 240.0), result)

    assert fixed.token_usage == usage
    assert fixed.tracked_attack_direction == "left"
    assert [e.time_sec for e in fixed.period_events] == [10.0, 1.0]
