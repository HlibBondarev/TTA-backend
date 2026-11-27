"""Deduplicates overlapping Pass 1 events and maps promptCodes to authoritative UUIDs."""

from __future__ import annotations

import re
from collections.abc import Sequence
from dataclasses import dataclass, field
from math import hypot, isfinite

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import (
    ACTING_TEAM_OPPONENT,
    ATTACK_DIRECTION_UNKNOWN,
    AttackDirection,
    LabelConflictGroup,
    Pass1ChunkResult,
    Pass1EventCandidate,
    Pass1RawEvent,
)
from video_analyzer.sport.events import EventDefinition
from video_analyzer.sport.profile import DefaultProfile, SportProfile

_MAX_COMPATIBLE_BBOX_CENTER_DIST: float = 0.25
_AMBIGUOUS_OVERLAP_MERGE_REASON: str = "AMBIGUOUS_OVERLAP_MERGE"
_OUT_OF_RANGE_EPSILON_SEC: float = 1e-6


@dataclass(slots=True)
class _MergedEntry:
    candidate: Pass1EventCandidate
    source_chunks: set[int] = field(default_factory=set)


@dataclass(slots=True)
class DeduplicationResult:
    """Deduplicated candidates plus the cross-chunk label-conflict groups to settle."""

    candidates: list[Pass1EventCandidate]
    raw_count: int
    warnings: list[str]
    conflict_groups: list[LabelConflictGroup] = field(default_factory=list)


class EventDeduplicator:
    """Resolves Pass 1 promptCodes to UUIDs and merges duplicates across chunk overlaps."""

    def __init__(self, merge_window_sec: float = 2.5) -> None:
        self._merge_window_sec = merge_window_sec

    def process(
        self,
        chunk_results: Sequence[tuple[VideoChunk, Pass1ChunkResult]],
        event_definitions: Sequence[EventDefinition],
        total_duration_sec: float,
        min_confidence: float,
        profile: SportProfile | None = None,
    ) -> tuple[list[Pass1EventCandidate], int, list[str]]:
        """Map promptCodes to EventDefinition and deduplicate overlap-zone events."""
        result = self.process_detailed(
            chunk_results, event_definitions, total_duration_sec, min_confidence, profile
        )
        return result.candidates, result.raw_count, result.warnings

    def process_detailed(
        self,
        chunk_results: Sequence[tuple[VideoChunk, Pass1ChunkResult]],
        event_definitions: Sequence[EventDefinition],
        total_duration_sec: float,
        min_confidence: float,
        profile: SportProfile | None = None,
    ) -> DeduplicationResult:
        """Like :meth:`process`, additionally returning cross-chunk label-conflict groups.

        Candidates whose acting team is the opponent are dropped unless ``profile`` marks the
        event as a conceded-type event (for example a conceded goal).
        """
        by_prompt_code = {item.prompt_code: item for item in event_definitions}
        sport_profile = profile or DefaultProfile()
        warnings: list[str] = []
        raw_candidates: list[tuple[int, Pass1EventCandidate]] = []
        chunk_bounds: dict[int, tuple[float, float]] = {}
        raw_count = 0

        for chunk, result in chunk_results:
            chunk_bounds[chunk.index] = (chunk.start_sec, chunk.end_sec)
            raw_count += len(result.period_events)
            for raw in result.period_events:
                candidate = self._to_candidate(
                    chunk,
                    raw,
                    result.tracked_attack_direction,
                    by_prompt_code,
                    sport_profile,
                    total_duration_sec,
                    min_confidence,
                    warnings,
                )
                if candidate is not None:
                    raw_candidates.append((chunk.index, candidate))

        merged = self._merge_entries(raw_candidates, chunk_bounds)
        return DeduplicationResult(
            candidates=sorted(
                (entry.candidate for entry in merged),
                key=lambda c: (c.period_number, c.video_time_sec),
            ),
            raw_count=raw_count,
            warnings=warnings,
            conflict_groups=self._find_conflict_groups(merged, chunk_bounds),
        )

    def _to_candidate(
        self,
        chunk: VideoChunk,
        raw: Pass1RawEvent,
        attack_direction: AttackDirection,
        by_prompt_code: dict[str, EventDefinition],
        profile: SportProfile,
        total_duration_sec: float,
        min_confidence: float,
        warnings: list[str],
    ) -> Pass1EventCandidate | None:
        """Convert one raw event to an absolute-time candidate, or None when it is discarded."""
        definition = by_prompt_code.get(raw.prompt_code)
        if definition is None:
            warnings.append(
                f"Ignored unrecognized Pass 1 promptCode '{raw.prompt_code}' "
                f"in chunk {chunk.index}."
            )
            return None

        chunk_duration = max(0.0, chunk.end_sec - chunk.start_sec)
        if raw.time_sec < 0.0 or raw.time_sec > chunk_duration + _OUT_OF_RANGE_EPSILON_SEC:
            warnings.append(
                f"Ignored out-of-range Pass 1 event offset {raw.time_sec:.3f}s "
                f"in chunk {chunk.index} (valid range: 0.000s..{chunk_duration:.3f}s)."
            )
            return None

        if raw.confidence < min_confidence:
            return None

        if raw.acting_team == ACTING_TEAM_OPPONENT and not profile.is_conceded_event(definition):
            warnings.append(
                f"Dropped opponent-team event '{definition.short_name}' at offset "
                f"{raw.time_sec:.3f}s in chunk {chunk.index}."
            )
            return None

        bbox = self._normalize_bbox(raw.actor_bbox_normalized)
        if raw.actor_bbox_normalized is not None and bbox is None:
            warnings.append(
                f"Discarded invalid actorBboxNormalized {raw.actor_bbox_normalized} for "
                f"'{definition.short_name}' at offset {raw.time_sec:.3f}s in chunk {chunk.index}."
            )
        abs_time = max(0.0, min(total_duration_sec, chunk.start_sec + raw.time_sec))
        return Pass1EventCandidate(
            video_time_sec=round(abs_time, 3),
            definition=definition,
            period_number=raw.period_number,
            is_lead_to_goal=raw.is_lead_to_goal,
            actor_bbox_normalized=bbox,
            pool_zone_hint=raw.pool_zone_hint,
            cap_color_hint=raw.cap_color_hint,
            confidence=raw.confidence,
            evidence=raw.evidence,
            acting_team=raw.acting_team,
            tracked_attack_direction=attack_direction,
        )

    def _find_conflict_groups(
        self,
        merged: list[_MergedEntry],
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> list[LabelConflictGroup]:
        """Group unmerged candidates from different chunks that disagree on the label."""
        ordered = sorted(merged, key=lambda e: e.candidate.video_time_sec)
        parent = list(range(len(ordered)))

        def find(index: int) -> int:
            while parent[index] != index:
                parent[index] = parent[parent[index]]
                index = parent[index]
            return index

        for left_idx, left in enumerate(ordered):
            for right_idx in range(left_idx + 1, len(ordered)):
                right = ordered[right_idx]
                gap = right.candidate.video_time_sec - left.candidate.video_time_sec
                if gap > self._merge_window_sec:
                    break
                if self._is_label_conflict(left, right, chunk_bounds):
                    parent[find(right_idx)] = find(left_idx)

        groups: dict[int, list[Pass1EventCandidate]] = {}
        for index, entry in enumerate(ordered):
            groups.setdefault(find(index), []).append(entry.candidate)
        return [
            LabelConflictGroup(candidates=members)
            for members in groups.values()
            if len(members) > 1
        ]

    def _is_label_conflict(
        self,
        first: _MergedEntry,
        second: _MergedEntry,
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> bool:
        left, right = first.candidate, second.candidate
        if left.definition.event_definition_id == right.definition.event_definition_id:
            return False
        if left.period_number != right.period_number or first.source_chunks & second.source_chunks:
            return False
        return any(
            self._shares_overlap_window(
                entry=first,
                chunk_index=chunk_index,
                candidate_time_sec=right.video_time_sec,
                chunk_bounds=chunk_bounds,
            )
            for chunk_index in second.source_chunks
        )

    def _merge_entries(
        self,
        candidates: list[tuple[int, Pass1EventCandidate]],
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> list[_MergedEntry]:
        ordered = sorted(
            candidates,
            key=lambda item: (item[1].video_time_sec, -item[1].confidence, item[0]),
        )
        merged: list[_MergedEntry] = []

        for chunk_index, candidate in ordered:
            match_idx = self._find_merge_target(merged, chunk_index, candidate, chunk_bounds)
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

        return merged

    def _find_merge_target(
        self,
        merged: list[_MergedEntry],
        chunk_index: int,
        candidate: Pass1EventCandidate,
        chunk_bounds: dict[int, tuple[float, float]],
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
            if not self._are_cross_chunk_actions_compatible(
                entry=entry,
                chunk_index=chunk_index,
                candidate=candidate,
                chunk_bounds=chunk_bounds,
            ):
                continue
            if best_delta is None or delta < best_delta:
                best_idx = idx
                best_delta = delta

        return best_idx

    def _are_cross_chunk_actions_compatible(
        self,
        entry: _MergedEntry,
        chunk_index: int,
        candidate: Pass1EventCandidate,
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> bool:
        prev = entry.candidate
        if prev.period_number != candidate.period_number:
            return False

        if not self._shares_overlap_window(
            entry=entry,
            chunk_index=chunk_index,
            candidate_time_sec=candidate.video_time_sec,
            chunk_bounds=chunk_bounds,
        ):
            return False

        prev_cap = _normalize_hint(prev.cap_color_hint)
        cand_cap = _normalize_hint(candidate.cap_color_hint)
        if prev_cap and cand_cap and prev_cap != cand_cap:
            return False

        prev_zone = _normalize_zone(prev.pool_zone_hint)
        cand_zone = _normalize_zone(candidate.pool_zone_hint)
        if prev_zone and cand_zone and prev_zone != cand_zone:
            return False

        if prev.actor_bbox_normalized is not None and candidate.actor_bbox_normalized is not None:
            iou = _norm_bbox_iou(prev.actor_bbox_normalized, candidate.actor_bbox_normalized)
            center_dist = _norm_bbox_center_dist(
                prev.actor_bbox_normalized,
                candidate.actor_bbox_normalized,
            )
            if iou <= 0.0 and center_dist > _MAX_COMPATIBLE_BBOX_CENTER_DIST:
                return False

        return True

    def _shares_overlap_window(
        self,
        entry: _MergedEntry,
        chunk_index: int,
        candidate_time_sec: float,
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> bool:
        c_bounds = chunk_bounds.get(chunk_index)
        if c_bounds is None:
            return True

        c_start, c_end = c_bounds
        prev_time_sec = entry.candidate.video_time_sec
        for src_chunk in entry.source_chunks:
            s_bounds = chunk_bounds.get(src_chunk)
            if s_bounds is None:
                continue
            overlap_start = max(c_start, s_bounds[0])
            overlap_end = min(c_end, s_bounds[1])
            if overlap_start >= overlap_end:
                continue
            low = overlap_start - self._merge_window_sec
            high = overlap_end + self._merge_window_sec
            if low <= prev_time_sec <= high and low <= candidate_time_sec <= high:
                return True
        return False

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
        if winner.tracked_attack_direction == ATTACK_DIRECTION_UNKNOWN:
            winner.tracked_attack_direction = loser.tracked_attack_direction
        winner.is_lead_to_goal = winner.is_lead_to_goal or loser.is_lead_to_goal
        # Optional Pass 1 hints cannot uniquely distinguish rapid back-to-back actions across
        # overlapping chunks, so any cross-chunk merge is flagged for Human-in-the-Loop review.
        winner.needs_review = True
        for reason in loser.review_reasons:
            if reason not in winner.review_reasons:
                winner.review_reasons.append(reason)
        if _AMBIGUOUS_OVERLAP_MERGE_REASON not in winner.review_reasons:
            winner.review_reasons.append(_AMBIGUOUS_OVERLAP_MERGE_REASON)
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
        vals = [float(v) for v in bbox]
        if not all(isfinite(v) for v in vals):
            return None
        x1, y1, x2, y2 = (round(max(0.0, min(1.0, v)), 4) for v in vals)
        if x2 <= x1 or y2 <= y1:
            return None
        return [x1, y1, x2, y2]


def _normalize_hint(value: str | None) -> str:
    return (value or "").strip().lower()


def _normalize_zone(value: str | None) -> str:
    return re.sub(r"[\s\-]+", "_", _normalize_hint(value))


def _norm_bbox_iou(box_a: list[float], box_b: list[float]) -> float:
    ax1, ay1, ax2, ay2 = box_a
    bx1, by1, bx2, by2 = box_b
    ix1 = max(ax1, bx1)
    iy1 = max(ay1, by1)
    ix2 = min(ax2, bx2)
    iy2 = min(ay2, by2)
    iw = max(0.0, ix2 - ix1)
    ih = max(0.0, iy2 - iy1)
    inter = iw * ih
    if inter <= 0.0:
        return 0.0
    area_a = max(0.0, ax2 - ax1) * max(0.0, ay2 - ay1)
    area_b = max(0.0, bx2 - bx1) * max(0.0, by2 - by1)
    union = area_a + area_b - inter
    return inter / union if union > 0.0 else 0.0


def _norm_bbox_center_dist(box_a: list[float], box_b: list[float]) -> float:
    acx = (box_a[0] + box_a[2]) * 0.5
    acy = (box_a[1] + box_a[3]) * 0.5
    bcx = (box_b[0] + box_b[2]) * 0.5
    bcy = (box_b[1] + box_b[3]) * 0.5
    return hypot(acx - bcx, acy - bcy)
