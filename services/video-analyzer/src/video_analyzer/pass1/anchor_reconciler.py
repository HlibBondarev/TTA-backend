"""Reconciles Pass 1 time anchors to guarantee valid TTA normalization invariants."""

from __future__ import annotations

from collections import defaultdict
from collections.abc import Sequence
from dataclasses import dataclass, field

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import Pass1AnchorCandidate, Pass1ChunkResult
from video_analyzer.schemas.draft import TimeAnchorType


@dataclass(slots=True)
class _IndexedAnchor:
    chunk_index: int
    anchor: Pass1AnchorCandidate


@dataclass(slots=True)
class _MergedAnchorEntry:
    anchor: Pass1AnchorCandidate
    min_time_sec: float
    max_time_sec: float
    source_chunks: set[int] = field(default_factory=set)


@dataclass(slots=True)
class _StoppageInterval:
    start: Pass1AnchorCandidate
    end: Pass1AnchorCandidate
    source_chunks: set[int] = field(default_factory=set)

    @property
    def combined_confidence(self) -> float:
        return self.start.confidence + self.end.confidence


class AnchorReconciler:
    """Deduplicates and reconciles period and stoppage anchors across overlapping chunks."""

    def __init__(self, merge_window_sec: float = 3.0) -> None:
        self._merge_window_sec = merge_window_sec

    def reconcile(
        self,
        chunk_results: Sequence[tuple[VideoChunk, Pass1ChunkResult]],
        total_duration_sec: float,
        min_confidence: float = 0.5,
    ) -> tuple[list[Pass1AnchorCandidate], list[str]]:
        """Deduplicate, enforce 1 PeriodStart + 1 PeriodEnd per period, and pair stoppages."""
        warnings: list[str] = []
        by_period: dict[int, list[_IndexedAnchor]] = defaultdict(list)
        chunk_bounds: dict[int, tuple[float, float]] = {}

        for chunk, result in chunk_results:
            chunk_bounds[chunk.index] = (chunk.start_sec, chunk.end_sec)
            chunk_duration = max(0.0, chunk.end_sec - chunk.start_sec)
            for raw in result.time_anchors:
                if raw.time_sec < 0.0 or raw.time_sec > chunk_duration + 1e-6:
                    warnings.append(
                        f"Ignored out-of-range Pass 1 anchor offset {raw.time_sec:.3f}s "
                        f"in chunk {chunk.index} (valid range: 0.000s..{chunk_duration:.3f}s)."
                    )
                    continue
                if raw.confidence < min_confidence:
                    continue
                abs_time = max(0.0, min(total_duration_sec, chunk.start_sec + raw.time_sec))
                by_period[raw.period_number].append(
                    _IndexedAnchor(
                        chunk_index=chunk.index,
                        anchor=Pass1AnchorCandidate(
                            video_time_sec=round(abs_time, 3),
                            period_number=raw.period_number,
                            type=TimeAnchorType(raw.type),
                            confidence=raw.confidence,
                            needs_review=raw.confidence < 0.80,
                            evidence=raw.evidence,
                        ),
                    )
                )

        by_period_reconciled: dict[int, list[Pass1AnchorCandidate]] = {}
        for period_num in sorted(by_period):
            period_anchors = self._reconcile_period(
                period_num=period_num,
                indexed_anchors=by_period[period_num],
                chunk_bounds=chunk_bounds,
                warnings=warnings,
            )
            if period_anchors:
                by_period_reconciled[period_num] = period_anchors

        self._flag_cross_period_overlaps(by_period_reconciled, warnings)

        reconciled: list[Pass1AnchorCandidate] = []
        for period_num in sorted(by_period_reconciled):
            reconciled.extend(by_period_reconciled[period_num])
        reconciled.sort(key=lambda a: (a.period_number, a.video_time_sec, a.type.value))
        return reconciled, warnings

    def _reconcile_period(
        self,
        period_num: int,
        indexed_anchors: list[_IndexedAnchor],
        chunk_bounds: dict[int, tuple[float, float]],
        warnings: list[str],
    ) -> list[Pass1AnchorCandidate]:
        starts = self._dedupe_cross_chunk_anchors(
            [item for item in indexed_anchors if item.anchor.type == TimeAnchorType.PERIOD_START],
            chunk_bounds,
        )
        ends = self._dedupe_cross_chunk_anchors(
            [item for item in indexed_anchors if item.anchor.type == TimeAnchorType.PERIOD_END],
            chunk_bounds,
        )

        selected_start = min(starts, key=lambda a: a.video_time_sec) if starts else None
        if len(starts) > 1:
            warnings.append(
                f"Period {period_num}: collapsed {len(starts)} PeriodStart candidates to earliest."
            )

        selected_end = max(ends, key=lambda a: a.video_time_sec) if ends else None
        if len(ends) > 1:
            warnings.append(
                f"Period {period_num}: collapsed {len(ends)} PeriodEnd candidates to latest."
            )

        if (
            selected_start
            and selected_end
            and selected_end.video_time_sec <= selected_start.video_time_sec
        ):
            warnings.append(
                f"Period {period_num}: PeriodEnd ({selected_end.video_time_sec}s) was not after "
                f"PeriodStart ({selected_start.video_time_sec}s); flagged for review."
            )
            selected_start.needs_review = True
            selected_end.needs_review = True

        result: list[Pass1AnchorCandidate] = []
        if selected_start:
            result.append(selected_start)

        lower_bound = selected_start.video_time_sec if selected_start else 0.0
        upper_bound = selected_end.video_time_sec if selected_end else float("inf")
        stoppage_items = [
            item
            for item in indexed_anchors
            if item.anchor.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END)
        ]
        paired_stoppages = self._reconcile_period_stoppages(
            period_num=period_num,
            stoppage_items=stoppage_items,
            lower_bound=lower_bound,
            upper_bound=upper_bound,
            chunk_bounds=chunk_bounds,
            warnings=warnings,
        )
        result.extend(paired_stoppages)

        if selected_end:
            result.append(selected_end)

        return result

    def _dedupe_cross_chunk_anchors(
        self,
        items: list[_IndexedAnchor],
        chunk_bounds: dict[int, tuple[float, float]],
        *,
        separator_times: Sequence[float] | None = None,
        separator_is_start: bool = True,
    ) -> list[Pass1AnchorCandidate]:
        ordered = sorted(items, key=lambda i: (i.anchor.video_time_sec, -i.anchor.confidence))
        merged: list[_MergedAnchorEntry] = []

        for item in ordered:
            target = self._find_anchor_merge_target(
                merged,
                item,
                chunk_bounds,
                separator_times=separator_times,
                separator_is_start=separator_is_start,
            )
            if target is None:
                merged.append(
                    _MergedAnchorEntry(
                        anchor=item.anchor,
                        min_time_sec=item.anchor.video_time_sec,
                        max_time_sec=item.anchor.video_time_sec,
                        source_chunks={item.chunk_index},
                    )
                )
                continue
            target.source_chunks.add(item.chunk_index)
            target.min_time_sec = min(target.min_time_sec, item.anchor.video_time_sec)
            target.max_time_sec = max(target.max_time_sec, item.anchor.video_time_sec)
            if item.anchor.confidence > target.anchor.confidence:
                target.anchor = item.anchor

        return [entry.anchor for entry in merged]

    def _find_anchor_merge_target(
        self,
        merged: list[_MergedAnchorEntry],
        item: _IndexedAnchor,
        chunk_bounds: dict[int, tuple[float, float]],
        *,
        separator_times: Sequence[float] | None = None,
        separator_is_start: bool = True,
    ) -> _MergedAnchorEntry | None:
        best_entry: _MergedAnchorEntry | None = None
        best_delta: float | None = None
        for entry in merged:
            if item.chunk_index in entry.source_chunks:
                continue
            delta = abs(item.anchor.video_time_sec - entry.anchor.video_time_sec)
            if delta > self._merge_window_sec:
                continue
            span_low = min(entry.min_time_sec, item.anchor.video_time_sec)
            span_high = max(entry.max_time_sec, item.anchor.video_time_sec)
            if self._has_separator(
                span_low,
                span_high,
                separator_times,
                is_start=separator_is_start,
            ):
                continue
            if not self._chunks_overlap_near_times(
                entry.source_chunks,
                item.chunk_index,
                [entry.anchor.video_time_sec, item.anchor.video_time_sec],
                chunk_bounds,
            ):
                continue
            if best_delta is None or delta < best_delta:
                best_entry = entry
                best_delta = delta
        return best_entry

    @staticmethod
    def _has_separator(
        t1: float,
        t2: float,
        separator_times: Sequence[float] | None,
        *,
        is_start: bool,
    ) -> bool:
        if not separator_times or t1 == t2:
            return False
        low, high = (t1, t2) if t1 < t2 else (t2, t1)
        if is_start:
            return any(low < sep <= high for sep in separator_times)
        return any(low <= sep < high for sep in separator_times)

    def _reconcile_period_stoppages(
        self,
        period_num: int,
        stoppage_items: list[_IndexedAnchor],
        lower_bound: float,
        upper_bound: float,
        chunk_bounds: dict[int, tuple[float, float]],
        warnings: list[str],
    ) -> list[Pass1AnchorCandidate]:
        in_bounds = [
            item
            for item in stoppage_items
            if lower_bound < item.anchor.video_time_sec < upper_bound
        ]
        dropped_out_of_bounds = len(stoppage_items) - len(in_bounds)
        if dropped_out_of_bounds > 0:
            warnings.append(
                f"Period {period_num}: ignored {dropped_out_of_bounds} stoppage anchor(s) "
                "outside period bounds."
            )

        by_chunk: dict[int, list[Pass1AnchorCandidate]] = defaultdict(list)
        raw_start_times: list[float] = []
        raw_end_times: list[float] = []
        for item in in_bounds:
            by_chunk[item.chunk_index].append(item.anchor)
            if item.anchor.type == TimeAnchorType.STOPPAGE_START:
                raw_start_times.append(item.anchor.video_time_sec)
            elif item.anchor.type == TimeAnchorType.STOPPAGE_END:
                raw_end_times.append(item.anchor.video_time_sec)

        chunk_intervals: list[_StoppageInterval] = []
        unpaired_starts: list[_IndexedAnchor] = []
        unpaired_ends: list[_IndexedAnchor] = []

        for chunk_idx in sorted(by_chunk):
            intervals, left_starts, left_ends = self._pair_within_chunk(
                chunk_idx,
                by_chunk[chunk_idx],
                all_start_times=raw_start_times,
                all_end_times=raw_end_times,
            )
            chunk_intervals.extend(intervals)
            unpaired_starts.extend(left_starts)
            unpaired_ends.extend(left_ends)

        deduped_intervals = self._dedupe_intervals(chunk_intervals, chunk_bounds)
        resolved_intervals = self._select_non_overlapping_intervals(deduped_intervals)

        all_end_times = [
            *(item.anchor.video_time_sec for item in unpaired_ends),
            *(iv.end.video_time_sec for iv in resolved_intervals),
        ]
        all_start_times = [
            *(item.anchor.video_time_sec for item in unpaired_starts),
            *(iv.start.video_time_sec for iv in resolved_intervals),
        ]

        remaining_starts = [
            item
            for item in unpaired_starts
            if not self._endpoint_matches_interval(
                item,
                resolved_intervals,
                chunk_bounds,
                is_start=True,
                separator_times=all_end_times,
            )
        ]
        remaining_ends = [
            item
            for item in unpaired_ends
            if not self._endpoint_matches_interval(
                item,
                resolved_intervals,
                chunk_bounds,
                is_start=False,
                separator_times=all_start_times,
            )
        ]

        deduped_starts = self._dedupe_cross_chunk_anchors(
            remaining_starts,
            chunk_bounds,
            separator_times=all_end_times,
            separator_is_start=True,
        )
        deduped_ends = self._dedupe_cross_chunk_anchors(
            remaining_ends,
            chunk_bounds,
            separator_times=all_start_times,
            separator_is_start=False,
        )

        cross_chunk_intervals = self._pair_cross_chunk_endpoints(
            period_num=period_num,
            starts=deduped_starts,
            ends=deduped_ends,
            existing_intervals=resolved_intervals,
            warnings=warnings,
        )
        all_intervals = sorted(
            [*resolved_intervals, *cross_chunk_intervals],
            key=lambda iv: (iv.start.video_time_sec, iv.end.video_time_sec),
        )
        self._flag_touching_stoppages(period_num, all_intervals, warnings)

        paired: list[Pass1AnchorCandidate] = []
        for iv in all_intervals:
            paired.append(iv.start)
            paired.append(iv.end)
        return paired

    @staticmethod
    def _flag_touching_stoppages(
        period_num: int,
        intervals: list[_StoppageInterval],
        warnings: list[str],
    ) -> None:
        for prev_iv, curr_iv in zip(intervals, intervals[1:], strict=False):
            if curr_iv.start.video_time_sec == prev_iv.end.video_time_sec:
                prev_iv.end.needs_review = True
                curr_iv.start.needs_review = True
                warnings.append(
                    f"Period {period_num}: adjacent stoppages share boundary timestamp "
                    f"{curr_iv.start.video_time_sec}s; flagged for review."
                )

    @staticmethod
    def _interval_contains_split(
        start_time: float,
        end_time: float,
        all_start_times: Sequence[float],
        all_end_times: Sequence[float],
    ) -> bool:
        inner_ends = [e for e in all_end_times if start_time < e < end_time]
        if not inner_ends:
            return False
        earliest_inner_end = min(inner_ends)
        return any(earliest_inner_end <= s < end_time for s in all_start_times)

    @staticmethod
    def _pair_within_chunk(
        chunk_index: int,
        anchors: list[Pass1AnchorCandidate],
        all_start_times: Sequence[float] = (),
        all_end_times: Sequence[float] = (),
    ) -> tuple[list[_StoppageInterval], list[_IndexedAnchor], list[_IndexedAnchor]]:
        starts = sorted(
            (a for a in anchors if a.type == TimeAnchorType.STOPPAGE_START),
            key=lambda a: a.video_time_sec,
        )
        ends = sorted(
            (a for a in anchors if a.type == TimeAnchorType.STOPPAGE_END),
            key=lambda a: a.video_time_sec,
        )

        intervals: list[_StoppageInterval] = []
        unpaired_starts: list[_IndexedAnchor] = []
        used_ends: set[int] = set()
        end_idx = 0
        last_end_time = float("-inf")

        for start in starts:
            if start.video_time_sec < last_end_time:
                continue
            while end_idx < len(ends) and ends[end_idx].video_time_sec <= start.video_time_sec:
                end_idx += 1
            if end_idx >= len(ends):
                unpaired_starts.append(_IndexedAnchor(chunk_index=chunk_index, anchor=start))
                continue
            end = ends[end_idx]
            if AnchorReconciler._interval_contains_split(
                start.video_time_sec,
                end.video_time_sec,
                all_start_times,
                all_end_times,
            ):
                unpaired_starts.append(_IndexedAnchor(chunk_index=chunk_index, anchor=start))
                continue
            used_ends.add(end_idx)
            end_idx += 1
            last_end_time = end.video_time_sec
            intervals.append(
                _StoppageInterval(start=start, end=end, source_chunks={chunk_index})
            )

        unpaired_ends = [
            _IndexedAnchor(chunk_index=chunk_index, anchor=end)
            for idx, end in enumerate(ends)
            if idx not in used_ends
        ]
        return intervals, unpaired_starts, unpaired_ends

    def _dedupe_intervals(
        self,
        intervals: list[_StoppageInterval],
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> list[_StoppageInterval]:
        ordered = sorted(
            intervals,
            key=lambda iv: (
                iv.start.video_time_sec,
                iv.end.video_time_sec,
                -iv.combined_confidence,
            ),
        )
        merged: list[_StoppageInterval] = []

        for iv in ordered:
            match_target = self._find_interval_merge_target(merged, iv, chunk_bounds)
            if match_target is None:
                merged.append(iv)
                continue
            match_target.source_chunks.update(iv.source_chunks)
            if iv.combined_confidence > match_target.combined_confidence:
                match_target.start = iv.start
                match_target.end = iv.end

        return merged

    def _find_interval_merge_target(
        self,
        merged: list[_StoppageInterval],
        candidate: _StoppageInterval,
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> _StoppageInterval | None:
        best_target: _StoppageInterval | None = None
        best_dist: float | None = None
        cand_chunk = next(iter(candidate.source_chunks))

        for existing in merged:
            if cand_chunk in existing.source_chunks:
                continue
            start_delta = abs(candidate.start.video_time_sec - existing.start.video_time_sec)
            end_delta = abs(candidate.end.video_time_sec - existing.end.video_time_sec)
            if start_delta > self._merge_window_sec or end_delta > self._merge_window_sec:
                continue
            # Distinct stoppages are temporally disjoint; repeated observations overlap in time.
            overlap = min(candidate.end.video_time_sec, existing.end.video_time_sec) - max(
                candidate.start.video_time_sec, existing.start.video_time_sec
            )
            if overlap <= 0.0:
                continue
            if not self._chunks_overlap_near_times(
                existing.source_chunks,
                cand_chunk,
                [
                    existing.start.video_time_sec,
                    existing.end.video_time_sec,
                    candidate.start.video_time_sec,
                    candidate.end.video_time_sec,
                ],
                chunk_bounds,
            ):
                continue
            dist = start_delta + end_delta
            if best_dist is None or dist < best_dist:
                best_target = existing
                best_dist = dist

        return best_target

    @staticmethod
    def _select_non_overlapping_intervals(
        intervals: list[_StoppageInterval],
    ) -> list[_StoppageInterval]:
        ordered = sorted(
            intervals,
            key=lambda iv: (
                iv.start.video_time_sec,
                iv.end.video_time_sec,
                -iv.combined_confidence,
            ),
        )
        accepted: list[_StoppageInterval] = []
        for iv in ordered:
            if accepted and iv.start.video_time_sec < accepted[-1].end.video_time_sec:
                if iv.combined_confidence > accepted[-1].combined_confidence:
                    accepted[-1] = iv
                continue
            accepted.append(iv)
        return accepted

    def _endpoint_matches_interval(
        self,
        item: _IndexedAnchor,
        intervals: list[_StoppageInterval],
        chunk_bounds: dict[int, tuple[float, float]],
        *,
        is_start: bool,
        separator_times: Sequence[float] | None = None,
    ) -> bool:
        for iv in intervals:
            if item.chunk_index in iv.source_chunks:
                continue
            ref = iv.start if is_start else iv.end
            if abs(item.anchor.video_time_sec - ref.video_time_sec) > self._merge_window_sec:
                continue
            if self._has_separator(
                item.anchor.video_time_sec,
                ref.video_time_sec,
                separator_times,
                is_start=is_start,
            ):
                continue
            if self._chunks_overlap_near_times(
                iv.source_chunks,
                item.chunk_index,
                [ref.video_time_sec, item.anchor.video_time_sec],
                chunk_bounds,
            ):
                return True
        return False

    @staticmethod
    def _pair_cross_chunk_endpoints(
        period_num: int,
        starts: list[Pass1AnchorCandidate],
        ends: list[Pass1AnchorCandidate],
        existing_intervals: list[_StoppageInterval],
        warnings: list[str],
    ) -> list[_StoppageInterval]:
        ordered_starts = sorted(starts, key=lambda a: a.video_time_sec)
        ordered_ends = sorted(ends, key=lambda a: a.video_time_sec)
        paired: list[_StoppageInterval] = []
        end_idx = 0

        for start_anchor in ordered_starts:
            occupied = any(
                iv.start.video_time_sec <= start_anchor.video_time_sec < iv.end.video_time_sec
                for iv in [*existing_intervals, *paired]
            )
            if occupied:
                continue
            while (
                end_idx < len(ordered_ends)
                and ordered_ends[end_idx].video_time_sec <= start_anchor.video_time_sec
            ):
                end_idx += 1
            if end_idx >= len(ordered_ends):
                warnings.append(
                    f"Period {period_num}: dropped unpaired StoppageStart "
                    f"at {start_anchor.video_time_sec}s."
                )
                continue
            end_anchor = ordered_ends[end_idx]
            conflicts = any(
                max(iv.start.video_time_sec, start_anchor.video_time_sec)
                < min(iv.end.video_time_sec, end_anchor.video_time_sec)
                for iv in [*existing_intervals, *paired]
            )
            if conflicts:
                warnings.append(
                    f"Period {period_num}: dropped unpaired StoppageStart "
                    f"at {start_anchor.video_time_sec}s."
                )
                continue
            end_idx += 1
            paired.append(_StoppageInterval(start=start_anchor, end=end_anchor))

        return paired

    def _chunks_overlap_near_times(
        self,
        existing_chunks: set[int],
        candidate_chunk: int,
        times_sec: list[float],
        chunk_bounds: dict[int, tuple[float, float]],
    ) -> bool:
        c_bounds = chunk_bounds.get(candidate_chunk)
        if c_bounds is None:
            return True
        c_start, c_end = c_bounds
        for src_chunk in existing_chunks:
            s_bounds = chunk_bounds.get(src_chunk)
            if s_bounds is None:
                continue
            overlap_start = max(c_start, s_bounds[0])
            overlap_end = min(c_end, s_bounds[1])
            if overlap_start >= overlap_end:
                continue
            low = overlap_start - self._merge_window_sec
            high = overlap_end + self._merge_window_sec
            if all(low <= t <= high for t in times_sec):
                return True
        return False

    @staticmethod
    def _flag_cross_period_overlaps(
        by_period_reconciled: dict[int, list[Pass1AnchorCandidate]],
        warnings: list[str],
    ) -> None:
        prev_period: int | None = None
        prev_end: Pass1AnchorCandidate | None = None

        for period_num in sorted(by_period_reconciled):
            curr_anchors = sorted(
                by_period_reconciled[period_num],
                key=lambda a: (a.video_time_sec, a.type.value),
            )
            curr_start = next(
                (a for a in curr_anchors if a.type == TimeAnchorType.PERIOD_START),
                curr_anchors[0],
            )
            if (
                prev_period is not None
                and prev_end is not None
                and curr_start.video_time_sec < prev_end.video_time_sec
            ):
                warnings.append(
                    f"Period {period_num}: {curr_start.type.value} ({curr_start.video_time_sec}s) "
                    f"precedes Period {prev_period} {prev_end.type.value} "
                    f"({prev_end.video_time_sec}s); flagged for review."
                )
                prev_end.needs_review = True
                curr_start.needs_review = True
                for anchor in curr_anchors:
                    if anchor.video_time_sec < prev_end.video_time_sec:
                        anchor.needs_review = True

            curr_end = next(
                (a for a in reversed(curr_anchors) if a.type == TimeAnchorType.PERIOD_END),
                curr_anchors[-1],
            )
            if prev_end is None or curr_end.video_time_sec >= prev_end.video_time_sec:
                prev_period = period_num
                prev_end = curr_end
