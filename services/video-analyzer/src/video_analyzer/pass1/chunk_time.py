"""Normalizes model-returned chunk offsets that were reported on the global video timeline."""

from __future__ import annotations

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import Pass1ChunkResult

_EPSILON_SEC: float = 1e-6
_TIME_DECIMALS: int = 3


def _returned_offsets(result: Pass1ChunkResult) -> list[float]:
    return [item.time_sec for item in result.period_events] + [
        item.time_sec for item in result.time_anchors
    ]


def _looks_global(chunk: VideoChunk, offsets: list[float]) -> bool:
    """Return True when every offset sits in the chunk's global window and one exceeds ``D``."""
    if chunk.start_sec <= 0.0 or not offsets:
        return False
    duration = max(0.0, chunk.end_sec - chunk.start_sec)
    exceeds_duration = any(value > duration + _EPSILON_SEC for value in offsets)
    inside_global_window = all(
        chunk.start_sec - _EPSILON_SEC <= value <= chunk.end_sec + _EPSILON_SEC for value in offsets
    )
    return exceeds_duration and inside_global_window


def _shift(value: float, start_sec: float) -> float:
    return round(max(0.0, value - start_sec), _TIME_DECIMALS)


def normalize_chunk_result(
    chunk: VideoChunk, result: Pass1ChunkResult
) -> tuple[Pass1ChunkResult, list[str]]:
    """Convert global-timeline offsets to chunk-relative offsets when the model ignored the rule.

    The shift is applied to a chunk only when ``chunk.start_sec > 0``, at least one returned
    offset exceeds the clip duration, and every returned offset lies inside
    ``[chunk.start_sec, chunk.end_sec]``. In every other case the result is returned unchanged
    so the per-item out-of-range handling downstream keeps working as before.

    Returns:
        The (possibly shifted) result and the warnings describing the normalization.
    """
    offsets = _returned_offsets(result)
    if not _looks_global(chunk, offsets):
        return result, []
    shifted = result.model_copy(
        update={
            "period_events": [
                event.model_copy(update={"time_sec": _shift(event.time_sec, chunk.start_sec)})
                for event in result.period_events
            ],
            "time_anchors": [
                anchor.model_copy(update={"time_sec": _shift(anchor.time_sec, chunk.start_sec)})
                for anchor in result.time_anchors
            ],
        }
    )
    warning = (
        f"Chunk {chunk.index}: normalized {len(offsets)} Pass 1 offset(s) from the global "
        f"timeline to chunk-relative time (subtracted {chunk.start_sec:.3f}s)."
    )
    return shifted, [warning]
