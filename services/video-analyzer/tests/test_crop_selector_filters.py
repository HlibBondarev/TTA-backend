"""Tests for profile-driven crop geometry and single-actor crop isolation in CropSelector."""

from __future__ import annotations

import numpy as np
import pytest

from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass2.color_constants import ASPECT_RATIO_TOLERANCE
from video_analyzer.pass2.crop_filters import (
    REJECT_AMBIGUOUS,
    REJECT_ASPECT_OUTLIER,
    REJECT_COLOR_MISMATCH,
    REJECT_FRAME_MISSING,
    REJECT_MAX_CROPS,
    REJECT_OUTSIDE_NEAR_WINDOW,
    CropSelectionContext,
    aspect_ratio_outlier_frames,
)
from video_analyzer.pass2.crop_selector import CropSelector
from video_analyzer.pass2.tracker import (
    Tracklet,
    TrackletObservation,
    compute_cap_crop_box,
    upper_body_cap_zone,
)
from video_analyzer.sport import DefaultProfile, WaterPoloProfile

WHITE = (255, 255, 255)
BLUE = (200, 40, 10)
BACKGROUND = (100, 100, 100)
EVENT_TIME = 20.0
PLAYER_BOX = (100.0, 50.0, 200.0, 170.0)
DEFAULT_ZONE = (110, 50, 190, 104)
FRAME_SIZE = (300, 400)


def _frame(
    index: int,
    time_sec: float,
    zones: list[tuple[tuple[int, int, int, int], tuple[int, int, int]]] | None = None,
    base: np.ndarray | None = None,
) -> ExtractedFrame:
    image = base.copy() if base is not None else np.full((*FRAME_SIZE, 3), BACKGROUND, np.uint8)
    for (x1, y1, x2, y2), color in zones or []:
        image[y1:y2, x1:x2] = color
    return ExtractedFrame(index=index, video_time_sec=time_sec, image_bgr=image)


def _obs(index: int, time_sec: float, box=PLAYER_BOX) -> TrackletObservation:
    return TrackletObservation(index, time_sec, box, 0.9, "Player")


def _times(count: int, step: float = 0.5, start: float = EVENT_TIME - 2.5) -> list[float]:
    return [start + step * i for i in range(count)]


def _context(**overrides) -> CropSelectionContext:
    values = {
        "event_time_sec": EVENT_TIME,
        "expected_color": "white",
        "opponent_color": "blue",
    }
    values.update(overrides)
    return CropSelectionContext(**values)


def _select(tracklet, frames, context, max_crops=12):
    report: list[dict] = []
    selected = CropSelector(min_frame_gap=1).select_and_build_sheet(
        tracklet, frames, max_crops=max_crops, selection_context=context, observation_report=report
    )
    return selected, report


def _reasons(report: list[dict]) -> dict[int, str]:
    return {r["frameIndex"]: r["reason"] for r in report if r["status"] == "rejected"}


# ---------------------------------------------------------------- profile-driven geometry


def test_water_polo_geometry_keeps_the_whole_head_and_shoulders_zone() -> None:
    default_zone = upper_body_cap_zone(PLAYER_BOX, DefaultProfile().cap_zone_geometry)
    water_polo_zone = upper_body_cap_zone(PLAYER_BOX, WaterPoloProfile().cap_zone_geometry)

    height = PLAYER_BOX[3] - PLAYER_BOX[1]
    assert default_zone[3] - default_zone[1] == pytest.approx(0.45 * height)
    assert water_polo_zone[3] - water_polo_zone[1] == pytest.approx(0.75 * height)
    # Caps sitting at 15-70% of the detector box height stay inside the water polo zone.
    assert water_polo_zone[3] >= PLAYER_BOX[1] + 0.70 * height
    assert default_zone[3] < PLAYER_BOX[1] + 0.70 * height


def test_default_profile_keeps_the_legacy_crop_geometry() -> None:
    geometry = DefaultProfile().cap_zone_geometry

    assert (geometry.top_fraction, geometry.horizontal_inset, geometry.padding) == (
        0.45,
        0.10,
        0.20,
    )
    assert compute_cap_crop_box(PLAYER_BOX) == compute_cap_crop_box(PLAYER_BOX, None, geometry)


def test_crop_selector_cuts_taller_crops_for_the_water_polo_profile() -> None:
    frame = _frame(0, EVENT_TIME).image_bgr
    obs = _obs(0, EVENT_TIME)

    default_crop = CropSelector._extract_cap_region(frame, obs, DefaultProfile().cap_zone_geometry)
    polo_crop = CropSelector._extract_cap_region(frame, obs, WaterPoloProfile().cap_zone_geometry)

    assert default_crop is not None
    assert polo_crop is not None
    assert polo_crop.shape[0] > default_crop.shape[0]
    assert polo_crop.shape[0] >= 0.75 * (PLAYER_BOX[3] - PLAYER_BOX[1])


# ------------------------------------------------------------------- near-event window


def test_crops_prefer_observations_within_one_second_of_the_event() -> None:
    times = _times(11)
    tracklet = Tracklet(1, [_obs(i, t) for i, t in enumerate(times)])
    frames = [_frame(i, t) for i, t in enumerate(times)]

    selected, report = _select(tracklet, frames, _context())

    assert selected is not None
    assert all(abs(c.video_time_sec - EVENT_TIME) <= 1.0 for c in selected.crops)
    assert len(selected.crops) == 5
    assert list(_reasons(report).values()) == [REJECT_OUTSIDE_NEAR_WINDOW] * 6


def test_crops_fall_back_to_the_full_window_when_too_few_near_observations_remain() -> None:
    times = [EVENT_TIME - 2.5, EVENT_TIME - 2.0, EVENT_TIME, EVENT_TIME + 2.0, EVENT_TIME + 2.5]
    tracklet = Tracklet(1, [_obs(i, t) for i, t in enumerate(times)])
    frames = [_frame(i, t) for i, t in enumerate(times)]

    selected, report = _select(tracklet, frames, _context())

    assert selected is not None
    assert len(selected.crops) == 5
    assert REJECT_OUTSIDE_NEAR_WINDOW not in _reasons(report).values()


def test_fallback_applies_after_color_filtering_leaves_one_near_observation() -> None:
    times = [EVENT_TIME - 2.0, EVENT_TIME - 0.5, EVENT_TIME + 0.5, EVENT_TIME + 2.0]
    tracklet = Tracklet(1, [_obs(i, t) for i, t in enumerate(times)])
    frames = [
        _frame(0, times[0], [(DEFAULT_ZONE, WHITE)]),
        _frame(1, times[1], [(DEFAULT_ZONE, BLUE)]),
        _frame(2, times[2], [(DEFAULT_ZONE, WHITE)]),
        _frame(3, times[3], [(DEFAULT_ZONE, WHITE)]),
    ]

    selected, report = _select(tracklet, frames, _context())

    assert selected is not None
    assert sorted(c.frame_index for c in selected.crops) == [0, 2, 3]
    assert _reasons(report) == {1: REJECT_COLOR_MISMATCH}


def test_closer_frames_win_when_sharpness_is_equal() -> None:
    noise = np.random.default_rng(7).integers(0, 255, (*FRAME_SIZE, 3), dtype=np.uint8)
    tracklet = Tracklet(1, [_obs(0, EVENT_TIME - 1.0), _obs(1, EVENT_TIME - 0.25)])
    frames = [_frame(0, EVENT_TIME - 1.0, base=noise), _frame(1, EVENT_TIME - 0.25, base=noise)]

    selected, _ = _select(tracklet, frames, _context(expected_color=None), max_crops=1)

    assert selected is not None
    assert [c.frame_index for c in selected.crops] == [1]


# --------------------------------------------------------------------- color mismatch


def test_observations_with_a_different_named_cap_color_are_dropped() -> None:
    times = [EVENT_TIME + 0.25 * i for i in range(5)]
    tracklet = Tracklet(1, [_obs(i, t) for i, t in enumerate(times)])
    colors = [WHITE, WHITE, BLUE, WHITE, BLUE]
    frames = [
        _frame(i, t, [(DEFAULT_ZONE, c)])
        for i, (t, c) in enumerate(zip(times, colors, strict=True))
    ]

    selected, report = _select(tracklet, frames, _context())

    assert selected is not None
    assert sorted(c.frame_index for c in selected.crops) == [0, 1, 3]
    assert _reasons(report) == {2: REJECT_COLOR_MISMATCH, 4: REJECT_COLOR_MISMATCH}


def test_observations_without_a_clear_named_color_are_kept() -> None:
    tracklet = Tracklet(1, [_obs(0, EVENT_TIME), _obs(1, EVENT_TIME + 0.25)])
    frames = [_frame(0, EVENT_TIME), _frame(1, EVENT_TIME + 0.25)]

    selected, report = _select(tracklet, frames, _context())

    assert selected is not None
    assert len(selected.crops) == 2
    assert _reasons(report) == {}


def test_unknown_expected_color_disables_the_color_filter() -> None:
    tracklet = Tracklet(1, [_obs(0, EVENT_TIME), _obs(1, EVENT_TIME + 0.25)])
    frames = [_frame(i, EVENT_TIME + 0.25 * i, [(DEFAULT_ZONE, BLUE)]) for i in range(2)]

    selected, _ = _select(tracklet, frames, _context(expected_color="magenta", opponent_color=None))

    assert selected is not None
    assert len(selected.crops) == 2


# ------------------------------------------------------------------ merged-box rejection


def test_merged_two_player_box_is_rejected_by_aspect_ratio() -> None:
    times = [EVENT_TIME + 0.25 * i for i in range(5)]
    merged = (60.0, 50.0, 280.0, 170.0)
    boxes = [PLAYER_BOX, PLAYER_BOX, merged, PLAYER_BOX, PLAYER_BOX]
    tracklet = Tracklet(
        1, [_obs(i, t, b) for i, (t, b) in enumerate(zip(times, boxes, strict=True))]
    )
    frames = [_frame(i, t) for i, t in enumerate(times)]

    selected, report = _select(tracklet, frames, _context(expected_color=None))

    assert selected is not None
    assert 2 not in {c.frame_index for c in selected.crops}
    assert _reasons(report) == {2: REJECT_ASPECT_OUTLIER}


def test_aspect_ratio_filter_needs_three_observations_and_never_drops_everything() -> None:
    narrow, wide = (0.0, 0.0, 50.0, 100.0), (0.0, 0.0, 200.0, 100.0)

    two = [_obs(0, 0.0, narrow), _obs(1, 0.25, wide)]
    assert aspect_ratio_outlier_frames(two) == set()

    uniform = [_obs(i, 0.25 * i, narrow) for i in range(4)]
    assert aspect_ratio_outlier_frames(uniform) == set()

    within_tolerance = (0.0, 0.0, 50.0 * (1 + ASPECT_RATIO_TOLERANCE / 2), 100.0)
    near = [_obs(0, 0.0, narrow), _obs(1, 0.25, narrow), _obs(2, 0.5, within_tolerance)]
    assert aspect_ratio_outlier_frames(near) == set()

    split = [_obs(0, 0.0, narrow), _obs(1, 0.25, narrow), _obs(2, 0.5, wide), _obs(3, 0.75, wide)]
    assert aspect_ratio_outlier_frames(split) == set()


# ----------------------------------------------------------- opponent neighbor ambiguity

NEIGHBOR_BOX = (180.0, 50.0, 280.0, 170.0)
NEIGHBOR_ZONE = (190, 50, 270, 104)


def _neighbor_case(neighbor_color, neighbor_box=NEIGHBOR_BOX, zone=NEIGHBOR_ZONE):
    target = Tracklet(1, [_obs(0, EVENT_TIME), _obs(1, EVENT_TIME + 0.25)])
    neighbor = Tracklet(
        2, [_obs(0, EVENT_TIME, neighbor_box), _obs(1, EVENT_TIME + 0.25, neighbor_box)]
    )
    frames = [
        _frame(i, EVENT_TIME + 0.25 * i, [(DEFAULT_ZONE, WHITE), (zone, neighbor_color)])
        for i in range(2)
    ]
    return _select(target, frames, _context(other_tracklets=(neighbor,)))


def test_overlapping_neighbor_with_opponent_cap_marks_the_crop_ambiguous() -> None:
    selected, _ = _neighbor_case(BLUE)

    assert selected is not None
    assert selected.has_ambiguous_cap is True
    assert all(c.cap_ambiguous for c in selected.crops)


def test_overlapping_neighbor_with_tracked_cap_is_not_ambiguous() -> None:
    selected, _ = _neighbor_case(WHITE)

    assert selected is not None
    assert selected.has_ambiguous_cap is False


def test_distant_opponent_neighbor_does_not_mark_the_crop_ambiguous() -> None:
    far_box = (300.0, 50.0, 390.0, 170.0)

    selected, _ = _neighbor_case(BLUE, neighbor_box=far_box, zone=(310, 50, 380, 104))

    assert selected is not None
    assert selected.has_ambiguous_cap is False


def test_unambiguous_crops_are_preferred_over_ambiguous_ones() -> None:
    target = Tracklet(1, [_obs(0, EVENT_TIME), _obs(1, EVENT_TIME + 0.25)])
    neighbor = Tracklet(2, [_obs(0, EVENT_TIME, NEIGHBOR_BOX)])
    frames = [
        _frame(0, EVENT_TIME, [(DEFAULT_ZONE, WHITE), (NEIGHBOR_ZONE, BLUE)]),
        _frame(1, EVENT_TIME + 0.25, [(DEFAULT_ZONE, WHITE)]),
    ]

    selected, _ = _select(target, frames, _context(other_tracklets=(neighbor,)))

    assert selected is not None
    assert selected.has_ambiguous_cap is False
    assert [c.frame_index for c in selected.crops] == [1]


def test_selection_without_context_keeps_legacy_behavior() -> None:
    tracklet = Tracklet(1, [_obs(0, EVENT_TIME), _obs(1, EVENT_TIME + 3.0)])
    frames = [_frame(0, EVENT_TIME, [(DEFAULT_ZONE, BLUE)]), _frame(1, EVENT_TIME + 3.0)]

    selected = CropSelector(min_frame_gap=1).select_and_build_sheet(tracklet, frames, max_crops=2)

    assert selected is not None
    assert len(selected.crops) == 2


# --------------------------------------- bi-colored goalkeeper crops accept the tracked base

RED = (20, 20, 220)
RED_PANEL_ZONE = (125, 56, 175, 62)  # small red panel at the top of the color zone


def _goalkeeper_context(**overrides) -> CropSelectionContext:
    values = {
        "expected_color": "red",
        "goalkeeper_color": "red",
        "tracked_color": "white",
        "opponent_color": "blue",
    }
    values.update(overrides)
    return _context(**values)


def _gk_frames(base_color, panel=True):
    times = [EVENT_TIME, EVENT_TIME + 0.25]
    zones = [(DEFAULT_ZONE, base_color)] + ([(RED_PANEL_ZONE, RED)] if panel else [])
    return Tracklet(1, [_obs(i, t) for i, t in enumerate(times)]), [
        _frame(i, t, zones) for i, t in enumerate(times)
    ]


def test_goalkeeper_event_keeps_white_base_bi_colored_goalkeeper_crops() -> None:
    tracklet, frames = _gk_frames(WHITE)

    selected, report = _select(tracklet, frames, _goalkeeper_context())

    assert selected is not None
    assert len(selected.crops) == 2
    assert REJECT_COLOR_MISMATCH not in _reasons(report).values()


def test_goalkeeper_event_rejects_blue_base_opponent_crops() -> None:
    tracklet, frames = _gk_frames(BLUE)

    selected, report = _select(tracklet, frames, _goalkeeper_context())

    assert selected is None
    assert set(_reasons(report).values()) == {REJECT_COLOR_MISMATCH}


def test_goalkeeper_event_keeps_solid_red_goalkeeper_crops() -> None:
    tracklet, frames = _gk_frames(RED, panel=False)

    selected, _ = _select(tracklet, frames, _goalkeeper_context())

    assert selected is not None
    assert len(selected.crops) == 2


def test_field_event_does_not_accept_a_goalkeeper_colored_head() -> None:
    tracklet, frames = _gk_frames(RED, panel=False)
    context = _goalkeeper_context(expected_color="white")

    selected, report = _select(tracklet, frames, context)

    assert selected is None
    assert set(_reasons(report).values()) == {REJECT_COLOR_MISMATCH}


def test_accepted_colors_include_the_tracked_base_only_for_goalkeeper_expectations() -> None:
    assert _goalkeeper_context().accepted_canonical == {"red", "white"}
    assert _goalkeeper_context(expected_color="white").accepted_canonical == {"white"}
    assert _goalkeeper_context(tracked_color=None).accepted_canonical == {"red"}
    assert _context().accepted_canonical == {"white"}


# ----------------------------------------------- the observation report covers everything


def test_observation_report_has_one_verdict_for_every_observation() -> None:
    neighbor_box = (170.0, 50.0, 270.0, 170.0)
    merged_box = (60.0, 50.0, 280.0, 170.0)
    times = [17.0, 19.5, 19.75, 20.0, 20.25, 20.5, 20.75, 21.0]
    boxes = {2: merged_box}
    tracklet = Tracklet(1, [_obs(i, t, boxes.get(i, PLAYER_BOX)) for i, t in enumerate(times)])
    neighbor = Tracklet(2, [_obs(4, times[4], neighbor_box)])
    white = [(DEFAULT_ZONE, WHITE)]
    frames_by_obs = {
        0: white,
        2: white,
        3: [(DEFAULT_ZONE, BLUE)],
        4: [(DEFAULT_ZONE, WHITE), (NEIGHBOR_ZONE, BLUE)],
        5: white,
        6: white,
        7: white,
    }
    frames = [_frame(i, times[i], zones) for i, zones in frames_by_obs.items()]  # no frame 1

    selected, report = _select(tracklet, frames, _context(other_tracklets=(neighbor,)), max_crops=2)

    assert selected is not None
    assert len(report) == len(times)
    assert sorted(entry["frameIndex"] for entry in report) == list(range(len(times)))
    by_frame = {entry["frameIndex"]: entry for entry in report}
    assert by_frame[0]["reason"] == REJECT_OUTSIDE_NEAR_WINDOW
    assert by_frame[1]["reason"] == REJECT_FRAME_MISSING
    assert by_frame[2]["reason"] == REJECT_ASPECT_OUTLIER
    assert by_frame[3]["reason"] == REJECT_COLOR_MISMATCH
    assert by_frame[4]["reason"] == REJECT_AMBIGUOUS
    statuses = [by_frame[i]["status"] for i in (5, 6, 7)]
    assert sorted(statuses) == ["rejected", "selected", "selected"]
    excluded = next(by_frame[i] for i in (5, 6, 7) if by_frame[i]["status"] == "rejected")
    assert excluded["reason"] == REJECT_MAX_CROPS
    assert {c.frame_index for c in selected.crops} == {
        i for i in (5, 6, 7) if by_frame[i]["status"] == "selected"
    }


def test_report_entries_are_unique_per_observation_without_a_context() -> None:
    times = [EVENT_TIME + 0.25 * i for i in range(4)]
    tracklet = Tracklet(1, [_obs(i, t) for i, t in enumerate(times)])
    frames = [_frame(i, t) for i, t in enumerate(times)]
    report: list[dict] = []

    selected = CropSelector(min_frame_gap=1).select_and_build_sheet(
        tracklet, frames, max_crops=3, observation_report=report
    )

    assert selected is not None
    assert len(report) == 4
    assert [entry["status"] for entry in sorted(report, key=lambda e: e["frameIndex"])].count(
        "selected"
    ) == 3
