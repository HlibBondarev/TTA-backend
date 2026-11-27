"""Tests for the deterministic dead-time event filter and the trailing StoppageStart rule."""

from __future__ import annotations

from pathlib import Path

from tests.support import CODE_GOAL, CODE_STEAL
from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.anchor_reconciler import AnchorReconciler
from video_analyzer.pass1.dead_time_filter import (
    UNVERIFIED_TRAILING_STOPPAGE_REASON,
    build_stoppage_intervals,
    filter_dead_time_events,
)
from video_analyzer.pass1.models import (
    Pass1AnchorCandidate,
    Pass1ChunkResult,
    Pass1EventCandidate,
    Pass1RawAnchor,
)
from video_analyzer.schemas.draft import TimeAnchorType
from video_analyzer.sport import AnalysisContext


def _anchor(
    time_sec: float, anchor_type: TimeAnchorType, period: int = 1, *, verified: bool = False
) -> Pass1AnchorCandidate:
    return Pass1AnchorCandidate(
        video_time_sec=time_sec,
        period_number=period,
        type=anchor_type,
        confidence=0.9,
        verified=verified,
    )


def _event(
    context: AnalysisContext, prompt_code: str, time_sec: float, period: int = 1
) -> Pass1EventCandidate:
    definition = next(d for d in context.event_catalog if d.prompt_code == prompt_code)
    return Pass1EventCandidate(
        video_time_sec=time_sec,
        definition=definition,
        period_number=period,
        is_lead_to_goal=False,
        actor_bbox_normalized=None,
        pool_zone_hint=None,
        cap_color_hint=None,
        confidence=0.9,
    )


def test_events_inside_a_closed_stoppage_are_dropped_with_a_warning(
    sample_context: AnalysisContext,
) -> None:
    anchors = [
        _anchor(60.0, TimeAnchorType.STOPPAGE_START),
        _anchor(90.0, TimeAnchorType.STOPPAGE_END),
    ]
    before = _event(sample_context, CODE_STEAL, 50.0)
    at_whistle = _event(sample_context, CODE_STEAL, 60.5)  # inside the stoppage grace
    inside = _event(sample_context, CODE_STEAL, 75.0)
    at_restart = _event(sample_context, CODE_STEAL, 90.0)  # StoppageEnd is exclusive
    after = _event(sample_context, CODE_STEAL, 120.0)

    kept, warnings = filter_dead_time_events(
        [before, at_whistle, inside, at_restart, after], anchors
    )

    assert kept == [before, at_whistle, at_restart, after]
    assert len(warnings) == 1
    assert "75.0s" in warnings[0]


def test_goal_events_inside_the_post_goal_grace_window_are_exempt(
    sample_context: AnalysisContext,
) -> None:
    anchors = [
        _anchor(60.0, TimeAnchorType.STOPPAGE_START),
        _anchor(90.0, TimeAnchorType.STOPPAGE_END),
    ]
    goal_soon = _event(sample_context, CODE_GOAL, 64.0)  # inside the 6s post-goal grace
    goal_late = _event(sample_context, CODE_GOAL, 75.0)  # well past it
    steal_soon = _event(sample_context, CODE_STEAL, 64.0)  # non-goal gets only the short grace

    kept, _ = filter_dead_time_events([goal_soon, goal_late, steal_soon], anchors)

    assert kept == [goal_soon]


def test_events_after_a_verified_trailing_stoppage_start_are_dropped(
    sample_context: AnalysisContext,
) -> None:
    anchors = [_anchor(100.0, TimeAnchorType.STOPPAGE_START, verified=True)]
    anchors[0].needs_review = True  # the reconciler flags every trailing start, even if verified
    live = _event(sample_context, CODE_STEAL, 80.0)
    dead = _event(sample_context, CODE_STEAL, 150.0)

    kept, warnings = filter_dead_time_events([live, dead], anchors)

    assert kept == [live]
    assert len(warnings) == 1


def test_events_after_an_unverified_trailing_stoppage_start_are_kept_and_flagged(
    sample_context: AnalysisContext,
) -> None:
    anchors = [_anchor(100.0, TimeAnchorType.STOPPAGE_START)]
    live = _event(sample_context, CODE_STEAL, 80.0)
    behind = _event(sample_context, CODE_STEAL, 150.0)

    kept, warnings = filter_dead_time_events([live, behind], anchors)

    assert kept == [live, behind]
    assert not live.needs_review
    assert behind.needs_review
    assert behind.review_reasons == [UNVERIFIED_TRAILING_STOPPAGE_REASON]
    assert len(warnings) == 1
    assert "unverified trailing StoppageStart" in warnings[0]


def test_unverified_trailing_flag_is_not_duplicated_on_repeated_filtering(
    sample_context: AnalysisContext,
) -> None:
    anchors = [_anchor(100.0, TimeAnchorType.STOPPAGE_START)]
    behind = _event(sample_context, CODE_STEAL, 150.0)

    filter_dead_time_events([behind], anchors)
    filter_dead_time_events([behind], anchors)

    assert behind.review_reasons == [UNVERIFIED_TRAILING_STOPPAGE_REASON]


def test_closed_stoppage_stays_authoritative_even_when_anchors_are_unverified(
    sample_context: AnalysisContext,
) -> None:
    anchors = [
        _anchor(60.0, TimeAnchorType.STOPPAGE_START),
        _anchor(90.0, TimeAnchorType.STOPPAGE_END),
    ]
    inside = _event(sample_context, CODE_STEAL, 75.0)

    kept, warnings = filter_dead_time_events([inside], anchors)

    assert kept == []
    assert len(warnings) == 1


def test_build_stoppage_intervals_can_exclude_unverified_trailing_starts() -> None:
    anchors = [
        _anchor(10.0, TimeAnchorType.STOPPAGE_START, verified=True),
        _anchor(20.0, TimeAnchorType.STOPPAGE_END),
        _anchor(40.0, TimeAnchorType.STOPPAGE_START),
    ]

    strict = build_stoppage_intervals(anchors, include_unverified_trailing=False)
    verified_trailing = build_stoppage_intervals(
        [*anchors[:2], _anchor(40.0, TimeAnchorType.STOPPAGE_START, verified=True)],
        include_unverified_trailing=False,
    )

    assert strict[1] == [(10.0, 20.0)]
    assert verified_trailing[1] == [(10.0, 20.0), (40.0, float("inf"))]


def test_a_later_start_supersedes_an_unpaired_start_instead_of_bridging_live_play() -> None:
    anchors = [
        _anchor(10.0, TimeAnchorType.STOPPAGE_START),  # its end was dropped by the verifier
        _anchor(50.0, TimeAnchorType.STOPPAGE_START),
        _anchor(60.0, TimeAnchorType.STOPPAGE_END),
    ]

    assert build_stoppage_intervals(anchors)[1] == [(50.0, 60.0)]


def test_start_with_dropped_end_does_not_wipe_live_play_before_the_next_stoppage(
    sample_context: AnalysisContext,
) -> None:
    anchors = [
        _anchor(10.0, TimeAnchorType.STOPPAGE_START, verified=True),
        _anchor(50.0, TimeAnchorType.STOPPAGE_START, verified=True),
        _anchor(60.0, TimeAnchorType.STOPPAGE_END, verified=True),
    ]
    live_between = _event(sample_context, CODE_STEAL, 30.0)
    inside_second = _event(sample_context, CODE_STEAL, 55.0)

    kept, warnings = filter_dead_time_events([live_between, inside_second], anchors)

    assert kept == [live_between]
    assert len(warnings) == 1


def test_two_closed_stoppages_keep_their_own_intervals() -> None:
    anchors = [
        _anchor(10.0, TimeAnchorType.STOPPAGE_START),
        _anchor(20.0, TimeAnchorType.STOPPAGE_END),
        _anchor(50.0, TimeAnchorType.STOPPAGE_START),
        _anchor(60.0, TimeAnchorType.STOPPAGE_END),
    ]

    assert build_stoppage_intervals(anchors)[1] == [(10.0, 20.0), (50.0, 60.0)]


def test_stoppages_only_affect_events_of_the_same_period(
    sample_context: AnalysisContext,
) -> None:
    anchors = [_anchor(100.0, TimeAnchorType.STOPPAGE_START, period=1)]
    other_period = _event(sample_context, CODE_STEAL, 150.0, period=2)

    kept, warnings = filter_dead_time_events([other_period], anchors)

    assert kept == [other_period]
    assert warnings == []


def test_build_stoppage_intervals_pairs_in_order_and_leaves_the_last_open() -> None:
    anchors = [
        _anchor(10.0, TimeAnchorType.STOPPAGE_START),
        _anchor(20.0, TimeAnchorType.STOPPAGE_END),
        _anchor(40.0, TimeAnchorType.STOPPAGE_START),
        _anchor(5.0, TimeAnchorType.PERIOD_START),
    ]

    intervals = build_stoppage_intervals(anchors)

    assert intervals[1][0] == (10.0, 20.0)
    assert intervals[1][1][0] == 40.0
    assert intervals[1][1][1] == float("inf")


def _reconcile(tmp_path: Path, anchors: list[Pass1RawAnchor]) -> list[Pass1AnchorCandidate]:
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    reconciled, _ = AnchorReconciler().reconcile(
        [(chunk, Pass1ChunkResult(time_anchors=anchors))], total_duration_sec=120.0
    )
    return reconciled


def _raw(time_sec: float, anchor_type: TimeAnchorType) -> Pass1RawAnchor:
    return Pass1RawAnchor(time_sec=time_sec, period_number=1, type=anchor_type, confidence=0.9)


def test_trailing_stoppage_start_without_period_end_is_kept_and_flagged(tmp_path: Path) -> None:
    reconciled = _reconcile(
        tmp_path,
        [
            _raw(5.0, TimeAnchorType.PERIOD_START),
            _raw(100.0, TimeAnchorType.STOPPAGE_START),
        ],
    )

    assert [a.type for a in reconciled] == [
        TimeAnchorType.PERIOD_START,
        TimeAnchorType.STOPPAGE_START,
    ]
    assert reconciled[1].needs_review is True


def test_unpaired_stoppage_start_before_period_end_is_still_dropped(tmp_path: Path) -> None:
    reconciled = _reconcile(
        tmp_path,
        [
            _raw(5.0, TimeAnchorType.PERIOD_START),
            _raw(100.0, TimeAnchorType.STOPPAGE_START),
            _raw(110.0, TimeAnchorType.PERIOD_END),
        ],
    )

    assert [a.type for a in reconciled] == [
        TimeAnchorType.PERIOD_START,
        TimeAnchorType.PERIOD_END,
    ]


def test_mid_period_paired_stoppage_is_unaffected_by_the_trailing_rule(tmp_path: Path) -> None:
    reconciled = _reconcile(
        tmp_path,
        [
            _raw(5.0, TimeAnchorType.PERIOD_START),
            _raw(40.0, TimeAnchorType.STOPPAGE_START),
            _raw(60.0, TimeAnchorType.STOPPAGE_END),
        ],
    )

    assert [a.type for a in reconciled] == [
        TimeAnchorType.PERIOD_START,
        TimeAnchorType.STOPPAGE_START,
        TimeAnchorType.STOPPAGE_END,
    ]
    assert all(not a.needs_review for a in reconciled)


def test_only_the_earliest_trailing_stoppage_start_is_kept(tmp_path: Path) -> None:
    reconciled = _reconcile(
        tmp_path,
        [
            _raw(5.0, TimeAnchorType.PERIOD_START),
            _raw(80.0, TimeAnchorType.STOPPAGE_START),
            _raw(100.0, TimeAnchorType.STOPPAGE_START),
        ],
    )

    starts = [a for a in reconciled if a.type == TimeAnchorType.STOPPAGE_START]
    assert [a.video_time_sec for a in starts] == [80.0]
    assert starts[0].needs_review is True
