"""Deterministic removal of gameplay events that fall inside confirmed dead time."""

from __future__ import annotations

import math
from collections import defaultdict
from collections.abc import Sequence

from video_analyzer.pass1.models import Pass1AnchorCandidate, Pass1EventCandidate
from video_analyzer.schemas.draft import TimeAnchorType

DEFAULT_STOPPAGE_GRACE_SEC: float = 1.5
DEFAULT_POST_GOAL_GRACE_SEC: float = 6.0

_Interval = tuple[float, float]


def build_stoppage_intervals(
    anchors: Sequence[Pass1AnchorCandidate],
) -> dict[int, list[_Interval]]:
    """Return ``[StoppageStart, StoppageEnd)`` intervals per period.

    A ``StoppageStart`` without a later ``StoppageEnd`` yields an open interval ending at
    infinity (the stoppage runs to the end of the period's footage).
    """
    by_period: dict[int, list[Pass1AnchorCandidate]] = defaultdict(list)
    for anchor in anchors:
        if anchor.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END):
            by_period[anchor.period_number].append(anchor)

    intervals: dict[int, list[_Interval]] = {}
    for period, period_anchors in by_period.items():
        ordered = sorted(period_anchors, key=lambda a: (a.video_time_sec, a.type.value))
        period_intervals: list[_Interval] = []
        open_start: float | None = None
        for anchor in ordered:
            if anchor.type == TimeAnchorType.STOPPAGE_START:
                open_start = anchor.video_time_sec if open_start is None else open_start
            elif open_start is not None:
                period_intervals.append((open_start, anchor.video_time_sec))
                open_start = None
        if open_start is not None:
            period_intervals.append((open_start, math.inf))
        intervals[period] = period_intervals
    return intervals


def _is_dead_time(
    event: Pass1EventCandidate,
    intervals: Sequence[_Interval],
    stoppage_grace_sec: float,
    post_goal_grace_sec: float,
) -> bool:
    """Return True when ``event`` sits inside a stoppage beyond its grace allowance."""
    is_goal_type = event.definition.score > 0
    grace = max(stoppage_grace_sec, post_goal_grace_sec) if is_goal_type else stoppage_grace_sec
    return any(start + grace <= event.video_time_sec < end for start, end in intervals)


def filter_dead_time_events(
    events: Sequence[Pass1EventCandidate],
    anchors: Sequence[Pass1AnchorCandidate],
    stoppage_grace_sec: float = DEFAULT_STOPPAGE_GRACE_SEC,
    post_goal_grace_sec: float = DEFAULT_POST_GOAL_GRACE_SEC,
) -> tuple[list[Pass1EventCandidate], list[str]]:
    """Drop gameplay events inside ``[StoppageStart + grace, StoppageEnd)`` of their period.

    Events after a trailing ``StoppageStart`` (no later ``StoppageEnd``) are dropped as well.
    Goal-type events (``score > 0``) get the longer ``post_goal_grace_sec`` allowance because the
    scoring action is often timestamped a few seconds after the stoppage whistle.

    Returns:
        The surviving events (input order preserved) and one warning per dropped event.
    """
    intervals = build_stoppage_intervals(anchors)
    kept: list[Pass1EventCandidate] = []
    warnings: list[str] = []
    for event in events:
        period_intervals = intervals.get(event.period_number, ())
        if _is_dead_time(event, period_intervals, stoppage_grace_sec, post_goal_grace_sec):
            warnings.append(
                f"Dropped dead-time event '{event.definition.short_name}' at "
                f"{event.video_time_sec}s (period {event.period_number}): inside a stoppage."
            )
            continue
        kept.append(event)
    return kept, warnings
