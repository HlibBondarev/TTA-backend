"""Deduplicates overlapping Pass 1 events and maps promptCodes to authoritative UUIDs."""

from __future__ import annotations

from collections.abc import Sequence

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1EventCandidate
from video_analyzer.schemas.request import EventDefinitionSnapshot


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
        raw_candidates: list[Pass1EventCandidate] = []
        raw_count = 0

        for chunk, result in chunk_results:
            for raw in result.period_events:
                raw_count += 1
                definition = by_prompt_code.get(raw.prompt_code)
                if definition is None:
                    warnings.append(
                        f"Ignored unrecognized Pass 1 promptCode '{raw.prompt_code}' "
                        f"in chunk {chunk.index}."
                    )
                    continue

                if raw.confidence < min_confidence:
                    continue

                abs_time = max(0.0, min(total_duration_sec, chunk.start_sec + raw.time_sec))
                bbox = self._normalize_bbox(raw.actor_bbox_normalized)
                raw_candidates.append(
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
                    )
                )

        deduplicated = self._merge_nearby(raw_candidates)
        return deduplicated, raw_count, warnings

    def _merge_nearby(
        self,
        candidates: list[Pass1EventCandidate],
    ) -> list[Pass1EventCandidate]:
        ordered = sorted(candidates, key=lambda c: (c.video_time_sec, -c.confidence))
        merged: list[Pass1EventCandidate] = []

        for candidate in ordered:
            match_idx = self._find_merge_target(merged, candidate)
            if match_idx is None:
                merged.append(candidate)
                continue

            existing = merged[match_idx]
            if candidate.confidence > existing.confidence:
                if candidate.actor_bbox_normalized is None:
                    candidate.actor_bbox_normalized = existing.actor_bbox_normalized
                if candidate.pool_zone_hint is None:
                    candidate.pool_zone_hint = existing.pool_zone_hint
                candidate.is_lead_to_goal = candidate.is_lead_to_goal or existing.is_lead_to_goal
                merged[match_idx] = candidate
            else:
                if existing.actor_bbox_normalized is None:
                    existing.actor_bbox_normalized = candidate.actor_bbox_normalized
                existing.is_lead_to_goal = existing.is_lead_to_goal or candidate.is_lead_to_goal

        return sorted(merged, key=lambda c: (c.period_number, c.video_time_sec))

    def _find_merge_target(
        self,
        merged: list[Pass1EventCandidate],
        candidate: Pass1EventCandidate,
    ) -> int | None:
        for idx in range(len(merged) - 1, -1, -1):
            prev = merged[idx]
            if abs(candidate.video_time_sec - prev.video_time_sec) > self._merge_window_sec:
                if candidate.video_time_sec - prev.video_time_sec > self._merge_window_sec:
                    break
                continue
            if prev.definition.event_definition_id == candidate.definition.event_definition_id:
                return idx
        return None

    @staticmethod
    def _normalize_bbox(bbox: list[float] | None) -> list[float] | None:
        if bbox is None or len(bbox) != 4:
            return None
        x1, y1, x2, y2 = (max(0.0, min(1.0, float(v))) for v in bbox)
        if x2 <= x1 or y2 <= y1:
            return None
        return [round(x1, 4), round(y1, 4), round(x2, 4), round(y2, 4)]
