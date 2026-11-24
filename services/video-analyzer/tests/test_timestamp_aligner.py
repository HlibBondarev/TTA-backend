"""Tests for T0 timestamp alignment and closed player presence invariants."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from pydantic import ValidationError

from video_analyzer.pipeline.timestamp_aligner import (
    resolve_video_start_utc,
    synthesize_closed_presences,
    to_utc_timestamp,
)
from video_analyzer.schemas.draft import (
    DraftEvent,
    DraftPlayerPresence,
    DraftTimeAnchor,
    TimeAnchorType,
)


def test_resolve_video_start_utc_prevents_future_timestamps() -> None:
    now = datetime(2026, 11, 23, 20, 0, 0, tzinfo=UTC)

    # Derived safe past when omitted
    derived, source, warnings = resolve_video_start_utc(None, duration_sec=3600.0, now_utc=now)
    assert source == "derived_safe_past"
    assert warnings == []
    assert derived + timedelta(seconds=3600.0) <= now

    # Clamped when caller sends a start time that would extend into the future
    clamped, source2, warnings2 = resolve_video_start_utc(
        now - timedelta(minutes=10), duration_sec=3600.0, now_utc=now
    )
    assert source2 == "clamped_from_future"
    assert len(warnings2) == 1
    assert clamped + timedelta(seconds=3600.0) <= now

    # Preserved intact when valid historical recording timestamp is provided
    historical = now - timedelta(hours=3)
    kept, source3, warnings3 = resolve_video_start_utc(historical, duration_sec=3600.0, now_utc=now)
    assert source3 == "provided"
    assert warnings3 == []
    assert kept == historical

    # Extreme future start timestamp must clamp without OverflowError
    max_future = datetime(9999, 12, 31, 23, 59, 59, tzinfo=UTC)
    clamped_max, source4, warnings4 = resolve_video_start_utc(
        max_future, duration_sec=3600.0, now_utc=now
    )
    assert source4 == "clamped_from_future"
    assert len(warnings4) == 1
    assert clamped_max + timedelta(seconds=3600.0) <= now

    # Exact boundary (requested + duration == now) remains provided
    exact_boundary = now - timedelta(seconds=3600.0)
    kept_boundary, source5, warnings5 = resolve_video_start_utc(
        exact_boundary, duration_sec=3600.0, now_utc=now
    )
    assert source5 == "provided"
    assert warnings5 == []
    assert kept_boundary == exact_boundary


def test_synthesize_closed_presences_defaults_empty_and_enforces_timeout_gt_timein() -> None:
    t0 = datetime(2026, 11, 23, 15, 0, 0, tzinfo=UTC)
    lineup_id = uuid4()
    anchors = [
        DraftTimeAnchor(
            id=uuid4(),
            period_number=1,
            type=TimeAnchorType.PERIOD_START,
            video_time_sec=10.0,
            timestamp=to_utc_timestamp(t0, 10.0),
            confidence=0.95,
        ),
        DraftTimeAnchor(
            id=uuid4(),
            period_number=1,
            type=TimeAnchorType.PERIOD_END,
            video_time_sec=500.0,
            timestamp=to_utc_timestamp(t0, 500.0),
            confidence=0.95,
        ),
    ]
    events = [
        DraftEvent(
            id=uuid4(),
            event_definition_id=uuid4(),
            prompt_code="EVT_01_G_POS",
            event_name="Goal",
            is_positive=True,
            is_shot=True,
            period_number=1,
            video_time_sec=120.0,
            event_timestamp=to_utc_timestamp(t0, 120.0),
            match_lineup_id=lineup_id,
            resolved_cap_number=7,
            event_confidence=0.9,
            cap_confidence=0.9,
            needs_review=False,
        )
    ]

    assert synthesize_closed_presences(events, anchors, t0, enabled=False) == []

    presences = synthesize_closed_presences(events, anchors, t0, enabled=True)
    assert len(presences) == 1
    assert presences[0].match_lineup_id == lineup_id
    assert presences[0].time_out > presences[0].time_in
    assert presences[0].time_in == to_utc_timestamp(t0, 10.0)
    assert presences[0].time_out == to_utc_timestamp(t0, 500.0)


def test_draft_player_presence_rejects_zero_or_negative_duration() -> None:
    t0 = datetime(2026, 11, 23, 15, 0, 0, tzinfo=UTC)
    with pytest.raises(ValidationError):
        DraftPlayerPresence(
            id=uuid4(),
            match_lineup_id=uuid4(),
            period_number=1,
            time_in_video_sec=10.0,
            time_out_video_sec=10.0,
            time_in=t0,
            time_out=t0,
            confidence=0.8,
        )


def test_synthesize_closed_presences_skips_intervals_collapsing_after_rounding() -> None:
    t0 = datetime(2026, 11, 23, 15, 0, 0, tzinfo=UTC)
    lineup_id = uuid4()
    anchors = [
        DraftTimeAnchor(
            id=uuid4(),
            period_number=1,
            type=TimeAnchorType.PERIOD_START,
            video_time_sec=10.0001,
            timestamp=to_utc_timestamp(t0, 10.0001),
            confidence=0.95,
        ),
        DraftTimeAnchor(
            id=uuid4(),
            period_number=1,
            type=TimeAnchorType.PERIOD_END,
            video_time_sec=10.0004,
            timestamp=to_utc_timestamp(t0, 10.0004),
            confidence=0.95,
        ),
        DraftTimeAnchor(
            id=uuid4(),
            period_number=2,
            type=TimeAnchorType.PERIOD_START,
            video_time_sec=0.0,
            timestamp=to_utc_timestamp(t0, 0.0),
            confidence=0.95,
        ),
        DraftTimeAnchor(
            id=uuid4(),
            period_number=2,
            type=TimeAnchorType.PERIOD_END,
            video_time_sec=0.0004,
            timestamp=to_utc_timestamp(t0, 0.0004),
            confidence=0.95,
        ),
    ]
    events = [
        DraftEvent(
            id=uuid4(),
            event_definition_id=uuid4(),
            prompt_code="EVT_01_G_POS",
            event_name="Goal",
            is_positive=True,
            is_shot=True,
            period_number=1,
            video_time_sec=10.0002,
            event_timestamp=to_utc_timestamp(t0, 10.0002),
            match_lineup_id=lineup_id,
            resolved_cap_number=7,
            event_confidence=0.9,
            cap_confidence=0.9,
            needs_review=False,
        ),
        DraftEvent(
            id=uuid4(),
            event_definition_id=uuid4(),
            prompt_code="EVT_01_G_POS",
            event_name="Goal",
            is_positive=True,
            is_shot=True,
            period_number=2,
            video_time_sec=0.0002,
            event_timestamp=to_utc_timestamp(t0, 0.0002),
            match_lineup_id=lineup_id,
            resolved_cap_number=7,
            event_confidence=0.9,
            cap_confidence=0.9,
            needs_review=False,
        ),
    ]

    presences = synthesize_closed_presences(events, anchors, t0, enabled=True)
    assert presences == []
