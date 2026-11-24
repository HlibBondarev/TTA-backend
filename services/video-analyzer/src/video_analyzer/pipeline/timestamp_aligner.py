"""T0 timestamp alignment and closed player-presence integrity helpers."""

from __future__ import annotations

from collections import defaultdict
from datetime import UTC, datetime, timedelta
from math import ceil
from typing import Literal
from uuid import UUID, uuid4

from video_analyzer.schemas.draft import (
    DraftEvent,
    DraftPlayerPresence,
    DraftTimeAnchor,
    TimeAnchorType,
)


def resolve_video_start_utc(
    requested_start_utc: datetime | None,
    duration_sec: float,
    now_utc: datetime | None = None,
) -> tuple[datetime, Literal["provided", "derived_safe_past", "clamped_from_future"], list[str]]:
    """Resolve a safe T0 UTC start so no event/anchor lands in the future during batch sync.

    TTA.BusinessLogic validators reject timestamps > DateTime.UtcNow + 5 minutes.
    Guaranteeing video_start_utc + duration_sec <= now_utc keeps every derived
    offset valid for sync-batch ingestion.
    """
    current_utc = _ensure_utc(now_utc or datetime.now(UTC))
    safe_duration = max(1.0, float(duration_sec))
    latest_valid_start = current_utc - timedelta(seconds=ceil(safe_duration) + 60)

    if requested_start_utc is None:
        return latest_valid_start, "derived_safe_past", []

    normalized = _ensure_utc(requested_start_utc)
    duration_delta = timedelta(seconds=safe_duration)
    if normalized >= current_utc or duration_delta > (current_utc - normalized):
        return (
            latest_valid_start,
            "clamped_from_future",
            [
                f"Provided videoStartUtc ({normalized.isoformat()}) plus duration "
                f"({safe_duration:.1f}s) would exceed current UTC time; clamped to "
                f"{latest_valid_start.isoformat()} to satisfy sync-batch timestamp bounds."
            ],
        )

    return normalized, "provided", []


def to_utc_timestamp(video_start_utc: datetime, video_time_sec: float) -> datetime:
    """Map a video offset in seconds to an absolute UTC TIMESTAMPTZ."""
    base = _ensure_utc(video_start_utc)
    offset = max(0.0, float(video_time_sec))
    return base + timedelta(seconds=offset)


def synthesize_closed_presences(
    events: list[DraftEvent],
    anchors: list[DraftTimeAnchor],
    video_start_utc: datetime,
    enabled: bool = False,
) -> list[DraftPlayerPresence]:
    """Generate strictly closed player presence intervals only when explicitly enabled.

    Never emits open (null timeOut) intervals for historical video timestamps, and
    strictly guarantees timeOut > timeIn as required by CreatePlayerPresenceRequestValidator.
    """
    if not enabled:
        return []

    period_bounds = _collect_period_bounds(anchors)
    if not period_bounds:
        return []

    observed_by_period: dict[int, set[UUID]] = defaultdict(set)
    for event in events:
        if event.match_lineup_id is not None:
            observed_by_period[event.period_number].add(event.match_lineup_id)

    presences: list[DraftPlayerPresence] = []
    for period_num in sorted(observed_by_period):
        bounds = period_bounds.get(period_num)
        if bounds is None:
            continue
        start_sec, end_sec = bounds
        rounded_start = round(max(0.0, start_sec), 3)
        rounded_end = round(max(0.0, end_sec), 3)
        if rounded_end <= rounded_start or rounded_end <= 0.0:
            continue

        time_in_dt = to_utc_timestamp(video_start_utc, rounded_start)
        time_out_dt = to_utc_timestamp(video_start_utc, rounded_end)
        if time_out_dt <= time_in_dt:
            continue

        for lineup_id in sorted(observed_by_period[period_num], key=str):
            presences.append(
                DraftPlayerPresence(
                    id=uuid4(),
                    match_lineup_id=lineup_id,
                    period_number=period_num,
                    time_in_video_sec=rounded_start,
                    time_out_video_sec=rounded_end,
                    time_in=time_in_dt,
                    time_out=time_out_dt,
                    confidence=0.70,
                    source="observed_in_period_events",
                )
            )

    return presences


def _collect_period_bounds(anchors: list[DraftTimeAnchor]) -> dict[int, tuple[float, float]]:
    starts: dict[int, float] = {}
    ends: dict[int, float] = {}
    for anchor in anchors:
        if anchor.type == TimeAnchorType.PERIOD_START:
            starts[anchor.period_number] = anchor.video_time_sec
        elif anchor.type == TimeAnchorType.PERIOD_END:
            ends[anchor.period_number] = anchor.video_time_sec

    bounds: dict[int, tuple[float, float]] = {}
    for period_num, start_sec in starts.items():
        end_sec = ends.get(period_num)
        if end_sec is not None and end_sec > start_sec:
            bounds[period_num] = (start_sec, end_sec)
    return bounds


def _ensure_utc(dt: datetime) -> datetime:
    if dt.tzinfo is None:
        return dt.replace(tzinfo=UTC)
    return dt.astimezone(UTC)
