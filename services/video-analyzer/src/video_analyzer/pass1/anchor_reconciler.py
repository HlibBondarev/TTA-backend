"""Reconciles Pass 1 time anchors to guarantee valid TTA normalization invariants."""

from __future__ import annotations

from collections import defaultdict
from collections.abc import Sequence

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import Pass1AnchorCandidate, Pass1ChunkResult
from video_analyzer.schemas.draft import TimeAnchorType


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
        raw_candidates: list[Pass1AnchorCandidate] = []

        for chunk, result in chunk_results:
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
                raw_candidates.append(
                    Pass1AnchorCandidate(
                        video_time_sec=round(abs_time, 3),
                        period_number=raw.period_number,
                        type=TimeAnchorType(raw.type),
                        confidence=raw.confidence,
                        needs_review=raw.confidence < 0.80,
                        evidence=raw.evidence,
                    )
                )

        deduped = self._merge_nearby_same_type(raw_candidates)
        by_period: dict[int, list[Pass1AnchorCandidate]] = defaultdict(list)
        for anchor in deduped:
            by_period[anchor.period_number].append(anchor)

        reconciled: list[Pass1AnchorCandidate] = []
        for period_num in sorted(by_period):
            reconciled.extend(self._reconcile_period(period_num, by_period[period_num], warnings))

        reconciled.sort(key=lambda a: (a.period_number, a.video_time_sec, a.type.value))
        return reconciled, warnings

    def _merge_nearby_same_type(
        self,
        candidates: list[Pass1AnchorCandidate],
    ) -> list[Pass1AnchorCandidate]:
        ordered = sorted(
            candidates,
            key=lambda a: (a.period_number, a.type.value, a.video_time_sec),
        )
        merged: list[Pass1AnchorCandidate] = []
        for item in ordered:
            if (
                merged
                and merged[-1].period_number == item.period_number
                and merged[-1].type == item.type
                and abs(merged[-1].video_time_sec - item.video_time_sec) <= self._merge_window_sec
            ):
                if item.confidence > merged[-1].confidence:
                    merged[-1] = item
                continue
            merged.append(item)
        return merged

    @staticmethod
    def _reconcile_period(
        period_num: int,
        anchors: list[Pass1AnchorCandidate],
        warnings: list[str],
    ) -> list[Pass1AnchorCandidate]:
        starts = [a for a in anchors if a.type == TimeAnchorType.PERIOD_START]
        ends = [a for a in anchors if a.type == TimeAnchorType.PERIOD_END]
        stop_starts = sorted(
            (a for a in anchors if a.type == TimeAnchorType.STOPPAGE_START),
            key=lambda a: a.video_time_sec,
        )
        stop_ends = sorted(
            (a for a in anchors if a.type == TimeAnchorType.STOPPAGE_END),
            key=lambda a: a.video_time_sec,
        )

        result: list[Pass1AnchorCandidate] = []

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

        if selected_start:
            result.append(selected_start)

        # Pair StoppageStart -> StoppageEnd strictly inside active period bounds
        lower_bound = selected_start.video_time_sec if selected_start else 0.0
        upper_bound = selected_end.video_time_sec if selected_end else float("inf")
        paired_stoppages = AnchorReconciler._pair_stoppages(
            period_num=period_num,
            stop_starts=stop_starts,
            stop_ends=stop_ends,
            lower_bound=lower_bound,
            upper_bound=upper_bound,
            warnings=warnings,
        )
        result.extend(paired_stoppages)

        if selected_end:
            result.append(selected_end)

        return result

    @staticmethod
    def _pair_stoppages(
        period_num: int,
        stop_starts: list[Pass1AnchorCandidate],
        stop_ends: list[Pass1AnchorCandidate],
        lower_bound: float,
        upper_bound: float,
        warnings: list[str],
    ) -> list[Pass1AnchorCandidate]:
        valid_starts = [s for s in stop_starts if lower_bound < s.video_time_sec < upper_bound]
        valid_ends = [e for e in stop_ends if lower_bound < e.video_time_sec < upper_bound]
        dropped_out_of_bounds = (len(stop_starts) - len(valid_starts)) + (
            len(stop_ends) - len(valid_ends)
        )
        if dropped_out_of_bounds > 0:
            warnings.append(
                f"Period {period_num}: ignored {dropped_out_of_bounds} stoppage anchor(s) "
                "outside period bounds."
            )

        paired: list[Pass1AnchorCandidate] = []
        end_idx = 0
        last_end_time = lower_bound

        for start_anchor in valid_starts:
            if start_anchor.video_time_sec <= last_end_time:
                continue
            while (
                end_idx < len(valid_ends)
                and valid_ends[end_idx].video_time_sec <= start_anchor.video_time_sec
            ):
                end_idx += 1
            if end_idx >= len(valid_ends):
                warnings.append(
                    f"Period {period_num}: dropped unpaired StoppageStart "
                    f"at {start_anchor.video_time_sec}s."
                )
                continue
            end_anchor = valid_ends[end_idx]
            end_idx += 1
            last_end_time = end_anchor.video_time_sec
            paired.append(start_anchor)
            paired.append(end_anchor)

        return paired
