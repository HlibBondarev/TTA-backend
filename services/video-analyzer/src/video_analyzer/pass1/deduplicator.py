"""Deduplicates overlapping Pass 1 events and maps promptCodes to authoritative UUIDs."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass, field

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1EventCandidate
from video_analyzer.schemas.request import EventDefinitionSnapshot


@dataclass(slots=True)
class _MergedEntry:
    candidate: Pass1EventCandidate
    source_chunks: set[int] = field(default_factory=set)


class EventDeduplicator:
    """Resolves Pass 1 promptCodes to UUIDs and merges duplicates across chunk overlaps."""

    def __init__(self, merge_window_sec: float = 2.5) -> None:
        self._merge_window_sec = merge_window_sec

    def process(
        self,
        chunk_results: Sequence[tuple[VideoChunk, Pass1ChunkResult]],
        event_definitions: Sequence[EventDefinitionSnapshot],
        total_duration_sec: float,
        min_confidence: float,
    ) -> tuple[list[Pass1EventCandidate], int, list[str]]:
        """Map promptCodes to EventDefinitionSnapshot and deduplicate overlap-zone events."""
        by_prompt_code = {item.prompt_code: item for item in event_definitions}
        warnings: list[str] = []
        raw_candidates: list[tuple[int, Pass1EventCandidate]] = []
        raw_count = 0

        for chunk, result in chunk_results:
            chunk_duration = max(0.0, chunk.end_sec - chunk.start_sec)
            for raw in result.period_events:
                raw_count += 1
                definition = by_prompt_code.get(raw.prompt_code)
                if definition is None:
                    warnings.append(
                        f"Ignored unrecognized Pass 1 promptCode '{raw.prompt_code}' "
                        f"in chunk {chunk.index}."
                    )
                    continue

                if raw.time_sec < 0.0 or raw.time_sec > chunk_duration + 1e-6:
                    warnings.append(
                        f"Ignored out-of-range Pass 1 event offset {raw.time_sec:.3f}s "
                        f"in chunk {chunk.index} (valid range: 0.000s..{chunk_duration:.3f}s)."
                    )
                    continue

                if raw.confidence < min_confidence:
                    continue

                abs_time = max(0.0, min(total_duration_sec, chunk.start_sec + raw.time_sec))
                bbox = self._normalize_bbox(raw.actor_bbox_normalized)
                raw_candidates.append(
                    (
                        chunk.index,
                        Pass1EventCandidate(
                            video_time_sec=round(abs_time, 3),
                            definition=definition,
                            period_number=raw.period_number,
                            is_lead_to_goal=raw.is_lead_to_goal,
                            actor_bbox_normalized=bbox,
                            pool_zone_hint=raw.pool_zone_hint,
                            cap_color_hint=raw.cap_color_hint,
                            confidence=raw.confidence,
                            evidence=raw.evidence,
                        ),
                    )
                )

        deduplicated = self._merge_nearby(raw_candidates)
        return deduplicated, raw_count, warnings

    def _merge_nearby(
        self,
        candidates: list[tuple[int, Pass1EventCandidate]],
    ) -> list[Pass1EventCandidate]:
        ordered = sorted(
            candidates,
            key=lambda item: (item[1].video_time_sec, -item[1].confidence, item[0]),
        )
        merged: list[_MergedEntry] = []

        for chunk_index, candidate in ordered:
            match_idx = self._find_merge_target(merged, chunk_index, candidate)
            if match_idx is None:
                merged.append(_MergedEntry(candidate=candidate, source_chunks={chunk_index}))
                continue

            entry = merged[match_idx]
            entry.source_chunks.add(chunk_index)
            existing = entry.candidate
            if candidate.confidence > existing.confidence:
                entry.candidate = self._merge_candidates(winner=candidate, loser=existing)
                self._reposition_entry(merged, match_idx)
            else:
                entry.candidate = self._merge_candidates(winner=existing, loser=candidate)

        return sorted(
            (entry.candidate for entry in merged),
            key=lambda c: (c.period_number, c.video_time_sec),
        )

    def _find_merge_target(
        self,
        merged: list[_MergedEntry],
        chunk_index: int,
        candidate: Pass1EventCandidate,
    ) -> int | None:
        best_idx: int | None = None
        best_delta: float | None = None

        for idx in range(len(merged) - 1, -1, -1):
            entry = merged[idx]
            prev = entry.candidate
            if candidate.video_time_sec - prev.video_time_sec > self._merge_window_sec:
                break
            delta = abs(candidate.video_time_sec - prev.video_time_sec)
            if delta > self._merge_window_sec:
                continue
            if chunk_index in entry.source_chunks:
                continue
            if prev.definition.event_definition_id != candidate.definition.event_definition_id:
                continue
            if best_delta is None or delta < best_delta:
                best_idx = idx
                best_delta = delta

        return best_idx

    @staticmethod
    def _merge_candidates(
        winner: Pass1EventCandidate,
        loser: Pass1EventCandidate,
    ) -> Pass1EventCandidate:
        if winner.video_time_sec == loser.video_time_sec and winner.actor_bbox_normalized is None:
            winner.actor_bbox_normalized = loser.actor_bbox_normalized
        if winner.pool_zone_hint is None:
            winner.pool_zone_hint = loser.pool_zone_hint
        if winner.cap_color_hint is None:
            winner.cap_color_hint = loser.cap_color_hint
        winner.is_lead_to_goal = winner.is_lead_to_goal or loser.is_lead_to_goal
        return winner

    @staticmethod
    def _reposition_entry(merged: list[_MergedEntry], idx: int) -> None:
        while idx + 1 < len(merged) and (
            merged[idx].candidate.video_time_sec > merged[idx + 1].candidate.video_time_sec
        ):
            merged[idx], merged[idx + 1] = merged[idx + 1], merged[idx]
            idx += 1
        while idx > 0 and (
            merged[idx - 1].candidate.video_time_sec > merged[idx].candidate.video_time_sec
        ):
            merged[idx - 1], merged[idx] = merged[idx], merged[idx - 1]
            idx -= 1

    @staticmethod
    def _normalize_bbox(bbox: list[float] | None) -> list[float] | None:
        if bbox is None or len(bbox) != 4:
            return None
        x1, y1, x2, y2 = (max(0.0, min(1.0, float(v))) for v in bbox)
        if x2 <= x1 or y2 <= y1:
            return None
        return [round(x1, 4), round(y1, 4), round(x2, 4), round(y2, 4)]
