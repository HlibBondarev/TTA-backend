"""Deterministic removal of gameplay events that fall inside confirmed dead time."""

from __future__ import annotations

import math
from collections import defaultdict
from collections.abc import Sequence

from video_analyzer.pass1.models import Pass1AnchorCandidate, Pass1EventCandidate
from video_analyzer.schemas.draft import TimeAnchorType

DEFAULT_STOPPAGE_GRACE_SEC: float = 1.5
DEFAULT_POST_GOAL_GRACE_SEC: float = 6.0

UNVERIFIED_TRAILING_STOPPAGE_REASON: str = "UNVERIFIED_TRAILING_STOPPAGE"

_Interval = tuple[float, float]


def _period_intervals(
    ordered: Sequence[Pass1AnchorCandidate], *, include_unverified_trailing: bool
) -> list[_Interval]:
    """Return the stoppage intervals of one period from its time-ordered stoppage anchors.

    A ``StoppageStart`` followed by another start before any ``StoppageEnd`` is unpaired (its end
    was lost) and is superseded: the later start opens its own interval, so an unpaired start can
    never bridge live play to pair with a later stoppage's end. A start left open at the end of
    the period yields ``(start, inf)`` unless it is unverified and
    ``include_unverified_trailing`` is False.
    """
    intervals: list[_Interval] = []
    open_anchor: Pass1AnchorCandidate | None = None
    for anchor in ordered:
        if anchor.type == TimeAnchorType.STOPPAGE_START:
            open_anchor = anchor
        elif open_anchor is not None:
            intervals.append((open_anchor.video_time_sec, anchor.video_time_sec))
            open_anchor = None
    if open_anchor is not None and (include_unverified_trailing or open_anchor.verified):
        intervals.append((open_anchor.video_time_sec, math.inf))
    return intervals


def build_stoppage_intervals(
    anchors: Sequence[Pass1AnchorCandidate],
    *,
    include_unverified_trailing: bool = True,
) -> dict[int, list[_Interval]]:
    """Return ``[StoppageStart, StoppageEnd)`` intervals per period.

    A ``StoppageStart`` without a later ``StoppageEnd`` yields an open interval ending at
    infinity (the stoppage runs to the end of the period's footage). When
    ``include_unverified_trailing`` is False, such an open interval is only produced for a
    trailing start the verifier confirmed.
    """
    by_period: dict[int, list[Pass1AnchorCandidate]] = defaultdict(list)
    for anchor in anchors:
        if anchor.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END):
            by_period[anchor.period_number].append(anchor)

    return {
        period: _period_intervals(
            sorted(period_anchors, key=lambda a: (a.video_time_sec, a.type.value)),
            include_unverified_trailing=include_unverified_trailing,
        )
        for period, period_anchors in by_period.items()
    }


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

    Events after a *verified* trailing ``StoppageStart`` (no later ``StoppageEnd``) are dropped as
    well. An unverified trailing start (verifier disabled, skipped or failed open) is not
    authoritative: the events behind it are kept and flagged for review instead of being lost.
    Goal-type events (``score > 0``) get the longer ``post_goal_grace_sec`` allowance because the
    scoring action is often timestamped a few seconds after the stoppage whistle.

    Returns:
        The surviving events (input order preserved) and one warning per dropped or flagged event.
    """
    strict = build_stoppage_intervals(anchors, include_unverified_trailing=False)
    lenient = build_stoppage_intervals(anchors, include_unverified_trailing=True)
    kept: list[Pass1EventCandidate] = []
    warnings: list[str] = []
    for event in events:
        if _is_dead_time(
            event, strict.get(event.period_number, ()), stoppage_grace_sec, post_goal_grace_sec
        ):
            warnings.append(
                f"Dropped dead-time event '{event.definition.short_name}' at "
                f"{event.video_time_sec}s (period {event.period_number}): inside a stoppage."
            )
            continue
        if _is_dead_time(
            event, lenient.get(event.period_number, ()), stoppage_grace_sec, post_goal_grace_sec
        ):
            _flag_unverified_stoppage(event)
            warnings.append(
                f"Kept event '{event.definition.short_name}' at {event.video_time_sec}s "
                f"(period {event.period_number}) behind an unverified trailing StoppageStart; "
                "flagged for review."
            )
        kept.append(event)
    return kept, warnings


def _flag_unverified_stoppage(event: Pass1EventCandidate) -> None:
    """Flag an event that sits behind an unverified trailing stoppage for human review."""
    event.needs_review = True
    if UNVERIFIED_TRAILING_STOPPAGE_REASON not in event.review_reasons:
        event.review_reasons.append(UNVERIFIED_TRAILING_STOPPAGE_REASON)
