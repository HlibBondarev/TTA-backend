"""Tests for sport-agnostic team classification (detector label votes and HSV cap-color votes)."""

from __future__ import annotations

import cv2
import numpy as np
import pytest

from tests.support import make_context
from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass2.color_constants import (
    OPPONENT_EXCLUSION_SHARE,
    canonical_color,
    colors_match,
)
from video_analyzer.pass2.team_classifier import (
    TeamClass,
    TeamClassificationSpec,
    TeamClassifier,
    TeamEvidence,
    dominant_head_color,
    head_zone_color_shares,
)
from video_analyzer.pass2.tracker import Tracklet, TrackletObservation
from video_analyzer.sport import DefaultProfile, WaterPoloProfile

# BGR patches: OpenCV hue 0-179, so pure blue is hue 120 and pure red is hue 0.
WHITE = (255, 255, 255)
BLUE = (200, 40, 10)
NAVY = (90, 20, 5)
RED = (20, 20, 220)
POOL_WATER = (200, 190, 40)
BACKGROUND = (100, 100, 100)

PLAYER_BOX = (50.0, 40.0, 150.0, 160.0)
# Default geometry head zone of PLAYER_BOX: x 60..140, y 40..94.
HEAD_ZONE = (60, 40, 140, 94)
EVENT_TIME = 10.0


def _spec(**overrides) -> TeamClassificationSpec:
    values = {
        "tracked_color": "white",
        "opponent_color": "blue",
        "goalkeeper_token": "goalkeeper",
        "goalkeeper_color": "red",
    }
    values.update(overrides)
    return TeamClassificationSpec(**values)


def _frame(index: int, color: tuple[int, int, int] | None) -> ExtractedFrame:
    image = np.full((200, 200, 3), BACKGROUND, dtype=np.uint8)
    if color is not None:
        x1, y1, x2, y2 = HEAD_ZONE
        image[y1:y2, x1:x2] = color
    return ExtractedFrame(index=index, video_time_sec=EVENT_TIME + 0.25 * index, image_bgr=image)


def _observation(index: int, label: str, time_sec: float | None = None) -> TrackletObservation:
    return TrackletObservation(
        frame_index=index,
        video_time_sec=EVENT_TIME + 0.25 * index if time_sec is None else time_sec,
        bbox_xyxy=PLAYER_BOX,
        confidence=0.9,
        raw_class_name=label,
    )


def _tracklet(labels: list[str]) -> Tracklet:
    return Tracklet(1, [_observation(i, label) for i, label in enumerate(labels)])


def _classify(labels: list[str], colors=None, spec=None):
    frames = None
    if colors is not None:
        frames = {i: _frame(i, color) for i, color in enumerate(colors)}
    return TeamClassifier().classify(_tracklet(labels), EVENT_TIME, frames, spec or _spec())


# --------------------------------------------------------------------- detector label votes


def test_label_votes_classify_tracked_team() -> None:
    evidence = _classify(["White team"] * 4)

    assert evidence.team_class is TeamClass.TRACKED
    assert evidence.confidence == pytest.approx(1.0)
    assert evidence.label_votes == {"tracked": 4}
    assert evidence.hsv_votes == {}


def test_label_votes_classify_opponent_at_sixty_percent_share() -> None:
    evidence = _classify(["Blue team"] * 3 + ["White team"] * 2)

    assert evidence.team_class is TeamClass.OPPONENT
    assert evidence.confidence == pytest.approx(0.6)


def test_label_votes_below_opponent_share_are_not_excluded() -> None:
    evidence = _classify(["Blue team"] * 2 + ["White team"] * 3)

    assert evidence.team_class is TeamClass.TRACKED


def test_goalkeeper_token_without_team_color_is_goalkeeper_with_evidence() -> None:
    evidence = _classify(["Goalkeeper"] * 3)

    assert evidence.team_class is TeamClass.GOALKEEPER
    assert evidence.goalkeeper_evidence is True


def test_team_colored_goalkeeper_label_is_our_goalkeeper_with_tracked_share() -> None:
    evidence = _classify(["White Goalkeeper"] * 3)

    assert evidence.team_class is TeamClass.GOALKEEPER
    assert evidence.goalkeeper_evidence is True
    assert evidence.tracked_share == pytest.approx(1.0)


def test_unlabeled_player_is_unknown_without_votes() -> None:
    evidence = _classify(["Player"] * 3)

    assert evidence.team_class is TeamClass.UNKNOWN
    assert evidence.confidence == 0.0
    assert evidence.goalkeeper_evidence is False


def test_team_colors_come_from_hints_not_from_hardcoded_names() -> None:
    spec = _spec(tracked_color="green", opponent_color="yellow", goalkeeper_token=None)

    assert _classify(["Green team"] * 2, spec=spec).team_class is TeamClass.TRACKED
    assert _classify(["Yellow team"] * 2, spec=spec).team_class is TeamClass.OPPONENT
    assert _classify(["White team"] * 2, spec=spec).team_class is TeamClass.UNKNOWN


def test_only_observations_near_event_time_vote() -> None:
    tracklet = Tracklet(
        1,
        [
            _observation(0, "Blue team", time_sec=EVENT_TIME - 3.0),
            _observation(1, "Blue team", time_sec=EVENT_TIME + 2.0),
            _observation(2, "White team", time_sec=EVENT_TIME + 0.5),
        ],
    )

    evidence = TeamClassifier().classify(tracklet, EVENT_TIME, None, _spec())

    assert evidence.team_class is TeamClass.TRACKED
    assert evidence.observation_count == 1


# -------------------------------------------------------------------- HSV cap-color voting


@pytest.mark.parametrize(
    ("patch", "expected"),
    [(WHITE, TeamClass.TRACKED), (BLUE, TeamClass.OPPONENT), (RED, TeamClass.GOALKEEPER)],
)
def test_hsv_votes_classify_unlabeled_players_by_cap_color(patch, expected) -> None:
    evidence = _classify(["Player"] * 3, colors=[patch] * 3)

    assert evidence.team_class is expected
    assert evidence.label_votes == {}
    assert evidence.hsv_votes == {str(expected): 3}


def test_navy_cap_votes_for_blue_opponent() -> None:
    evidence = _classify(["Player"] * 3, colors=[NAVY] * 3, spec=_spec(opponent_color="navy"))

    assert evidence.team_class is TeamClass.OPPONENT


def test_pool_water_does_not_vote_for_blue_or_any_named_cap_color() -> None:
    evidence = _classify(["Player"] * 3, colors=[POOL_WATER] * 3)

    assert evidence.team_class is TeamClass.UNKNOWN
    assert evidence.hsv_votes == {}


def test_hsv_vote_overrides_missing_label_evidence_to_exclude_opponent_cap() -> None:
    evidence = _classify(["Player", "Player", "White team"], colors=[BLUE, BLUE, BLUE])

    assert evidence.team_class is TeamClass.OPPONENT
    assert evidence.hsv_votes == {"opponent": 3}


def test_unknown_color_names_are_skipped_silently() -> None:
    spec = _spec(tracked_color="magenta", opponent_color="chartreuse", goalkeeper_color=None)

    evidence = _classify(["Player"] * 2, colors=[WHITE, BLUE], spec=spec)

    assert evidence.team_class is TeamClass.UNKNOWN
    assert evidence.hsv_votes == {}
    assert spec.hsv_palette() == {}


def test_hsv_is_disabled_without_frames() -> None:
    evidence = TeamClassifier().classify(_tracklet(["Player"] * 3), EVENT_TIME, None, _spec())

    assert evidence.team_class is TeamClass.UNKNOWN


def test_detected_cap_box_is_preferred_over_estimated_head_zone() -> None:
    frame = _frame(0, None)
    frame.image_bgr[120:150, 70:130] = BLUE
    obs = TrackletObservation(
        0, EVENT_TIME, PLAYER_BOX, 0.9, "Player", cap_bbox_xyxy=(70.0, 120.0, 130.0, 150.0)
    )

    evidence = TeamClassifier().classify(Tracklet(1, [obs]), EVENT_TIME, {0: frame}, _spec())

    assert evidence.team_class is TeamClass.OPPONENT


# ------------------------------------------------------------------------- helpers and spec


def test_head_zone_color_shares_and_dominant_color() -> None:
    image = _frame(0, WHITE).image_bgr

    shares = head_zone_color_shares(image, tuple(float(v) for v in HEAD_ZONE), ["white", "blue"])

    assert shares["white"] == pytest.approx(1.0)
    assert shares["blue"] == pytest.approx(0.0)
    assert dominant_head_color(image, tuple(float(v) for v in HEAD_ZONE), ["white", "blue"]) == (
        "white"
    )
    assert head_zone_color_shares(image, (0.0, 0.0, 2.0, 2.0), ["white"]) == {}


def test_color_aliases_and_matching() -> None:
    assert canonical_color("Navy") == "blue"
    assert canonical_color("dark-blue") == "blue"
    assert canonical_color("magenta") is None
    assert colors_match("navy", "Blue")
    assert colors_match("teal", " TEAL ")
    assert not colors_match("white", "blue")
    assert not colors_match(None, "white")


def test_spec_from_context_reads_hints_and_profile_without_hardcoding() -> None:
    water_polo = TeamClassificationSpec.from_context(make_context(profile=WaterPoloProfile()))
    neutral = TeamClassificationSpec.from_context(make_context(profile=DefaultProfile()))

    assert (water_polo.tracked_color, water_polo.opponent_color) == ("white", "blue")
    assert water_polo.goalkeeper_token == "goalkeeper"
    assert water_polo.goalkeeper_color == "red"
    assert neutral.goalkeeper_token is None
    assert neutral.goalkeeper_color is None
    assert water_polo.geometry.top_fraction > neutral.geometry.top_fraction


# ------------------------------------------------- weighted votes: HSV outweighs noisy labels


def _hsv_bgr(hue: int, sat: int, val: int) -> tuple[int, int, int]:
    pixel = cv2.cvtColor(np.uint8([[[hue, sat, val]]]), cv2.COLOR_HSV2BGR)[0, 0]
    return int(pixel[0]), int(pixel[1]), int(pixel[2])


# Colors measured on dim Clip 1 footage (see color_constants for the calibration notes).
DIM_WATER = _hsv_bgr(101, 105, 97)
DIM_NAVY_CAP = _hsv_bgr(106, 56, 76)
DIM_WHITE_CAP = _hsv_bgr(25, 30, 118)
WARM_SKIN = _hsv_bgr(10, 80, 140)
CRIMSON = _hsv_bgr(176, 200, 140)

# Color zone of PLAYER_BOX: middle 50% of the width, top 5%-50% of the height.
COLOR_ZONE = (75, 46, 125, 100)


def _zone_frame(index: int, base, panel=None) -> ExtractedFrame:
    """Frame whose color zone is ``base``, with an optional ``(height_share, color)`` panel."""
    image = np.full((200, 200, 3), BACKGROUND, dtype=np.uint8)
    x1, y1, x2, y2 = COLOR_ZONE
    image[y1:y2, x1:x2] = base
    if panel is not None:
        share, color = panel
        image[y1 : y1 + max(1, int(round((y2 - y1) * share))), x1:x2] = color
    return ExtractedFrame(index=index, video_time_sec=EVENT_TIME + 0.25 * index, image_bgr=image)


def _classify_zone(labels, base, panel=None, spec=None) -> TeamEvidence:
    frames = {i: _zone_frame(i, base, panel) for i in range(len(labels))}
    return TeamClassifier().classify(_tracklet(labels), EVENT_TIME, frames, spec or _spec())


def test_white_labeled_tracklet_with_blue_cap_color_is_classified_as_opponent() -> None:
    evidence = _classify(["White team"] * 4, colors=[BLUE] * 4)

    assert evidence.team_class is TeamClass.OPPONENT
    assert evidence.confidence == pytest.approx(0.65)
    assert evidence.confidence >= OPPONENT_EXCLUSION_SHARE
    assert evidence.label_votes == {"tracked": 4}
    assert evidence.hsv_votes == {"opponent": 4}


def test_navy_cap_mislabeled_white_is_opponent_on_dim_footage_colors() -> None:
    evidence = _classify_zone(["White team"] * 3, DIM_NAVY_CAP)

    assert evidence.team_class is TeamClass.OPPONENT


def test_label_only_classification_is_unchanged_without_frames() -> None:
    labeled = TeamClassifier().classify(_tracklet(["White team"] * 4), EVENT_TIME, None, _spec())

    assert labeled.team_class is TeamClass.TRACKED
    assert labeled.confidence == pytest.approx(1.0)


def test_sparse_opponent_hsv_votes_do_not_flip_a_consistently_labeled_tracklet() -> None:
    labels = ["White team"] * 5
    frames = {0: _frame(0, BLUE), 1: _frame(1, BLUE), 2: _frame(2, None)}
    tracklet = Tracklet(1, [_observation(i, label) for i, label in enumerate(labels)])

    evidence = TeamClassifier().classify(tracklet, EVENT_TIME, frames, _spec())

    assert evidence.hsv_votes == {"opponent": 2}
    assert evidence.team_class is TeamClass.TRACKED


def test_agreeing_label_and_hsv_votes_reinforce_each_other() -> None:
    evidence = _classify(["White team"] * 3, colors=[WHITE] * 3)

    assert evidence.team_class is TeamClass.TRACKED
    assert evidence.confidence == pytest.approx(1.0)


# ---------------------------------------------------- calibrated colors: water vs caps


def test_dim_pool_water_never_votes_for_any_cap_color() -> None:
    evidence = _classify_zone(["Player"] * 3, DIM_WATER)

    assert evidence.team_class is TeamClass.UNKNOWN
    assert evidence.hsv_votes == {}


def test_dim_white_cap_votes_for_the_tracked_team() -> None:
    evidence = _classify_zone(["Player"] * 3, DIM_WHITE_CAP)

    assert evidence.team_class is TeamClass.TRACKED
    assert evidence.hsv_votes == {"tracked": 3}


def test_dim_navy_cap_votes_for_the_blue_opponent() -> None:
    evidence = _classify_zone(["Player"] * 3, DIM_NAVY_CAP)

    assert evidence.team_class is TeamClass.OPPONENT
    assert evidence.hsv_votes == {"opponent": 3}


def test_cap_color_is_sampled_in_the_top_center_of_the_box_only() -> None:
    image = np.full((200, 200, 3), DIM_WATER, dtype=np.uint8)
    image[46:100, 75:125] = WHITE
    image[130:160, 50:150] = BLUE  # shoulders/water below the cap zone must not vote
    frame = ExtractedFrame(index=0, video_time_sec=EVENT_TIME, image_bgr=image)
    tracklet = Tracklet(1, [_observation(0, "Player")])

    evidence = TeamClassifier().classify(tracklet, EVENT_TIME, {0: frame}, _spec())

    assert evidence.hsv_votes == {"tracked": 1}


# ------------------------------------------------------- bi-colored goalkeeper caps


def test_tracked_base_with_red_panels_is_our_goalkeeper() -> None:
    evidence = _classify_zone(["Player"] * 3, WHITE, panel=(0.3, CRIMSON))

    assert evidence.team_class is TeamClass.GOALKEEPER
    assert evidence.goalkeeper_evidence is True
    assert evidence.hsv_votes == {"tracked": 3}
    assert evidence.tracked_share == pytest.approx(1.0)


def test_detector_label_white_team_does_not_hide_red_goalkeeper_panels() -> None:
    evidence = _classify_zone(["White team"] * 3, WHITE, panel=(0.3, CRIMSON))

    assert evidence.team_class is TeamClass.GOALKEEPER
    assert evidence.goalkeeper_evidence is True


def test_opponent_base_with_red_panels_is_the_opponent_goalkeeper() -> None:
    evidence = _classify_zone(["Player"] * 3, BLUE, panel=(0.3, CRIMSON))

    assert evidence.team_class is TeamClass.OPPONENT
    assert evidence.goalkeeper_evidence is True


def test_opponent_labeled_goalkeeper_stays_opponent() -> None:
    evidence = _classify(["Blue Goalkeeper"] * 3)

    assert evidence.team_class is TeamClass.OPPONENT
    assert evidence.goalkeeper_evidence is True


def test_solid_red_cap_remains_our_goalkeeper_candidate() -> None:
    evidence = _classify_zone(["Player"] * 3, CRIMSON)

    assert evidence.team_class is TeamClass.GOALKEEPER
    assert evidence.hsv_votes == {"goalkeeper": 3}


def test_tiny_red_patch_below_the_panel_share_is_not_goalkeeper_evidence() -> None:
    evidence = _classify_zone(["Player"] * 3, WHITE, panel=(0.04, CRIMSON))

    assert evidence.goalkeeper_evidence is False
    assert evidence.team_class is TeamClass.TRACKED


def test_warm_skin_tones_are_not_goalkeeper_panels() -> None:
    evidence = _classify_zone(["Player"] * 3, WHITE, panel=(0.5, WARM_SKIN))

    assert evidence.goalkeeper_evidence is False
    assert evidence.team_class is TeamClass.TRACKED


def test_goalkeeper_color_equal_to_a_team_color_gives_no_panel_evidence() -> None:
    spec = _spec(tracked_color="red", goalkeeper_color="red")

    evidence = _classify_zone(["Player"] * 3, WHITE, panel=(0.3, CRIMSON), spec=spec)

    assert spec.goalkeeper_panel_color() is None
    assert evidence.goalkeeper_evidence is False


def test_goalkeeper_label_token_is_not_discounted_by_a_team_color_cap_vote() -> None:
    evidence = _classify_zone(["Goalkeeper"] * 3, DIM_WHITE_CAP)

    assert evidence.goalkeeper_evidence is True
    assert evidence.team_class is TeamClass.GOALKEEPER


# ------------------------------- asymmetric guard: thin HSV evidence vs explicit opponent label


def test_single_hsv_tracked_frame_does_not_overrule_an_explicit_opponent_label() -> None:
    evidence = _classify(["Blue team"], colors=[WHITE])

    assert evidence.team_class is TeamClass.UNKNOWN
    assert evidence.label_votes == {"opponent": 1}
    assert evidence.hsv_votes == {"tracked": 1}


def test_two_hsv_tracked_frames_do_overrule_an_explicit_opponent_label() -> None:
    evidence = _classify(["Blue team"] * 2, colors=[WHITE] * 2)

    assert evidence.team_class is TeamClass.TRACKED
    assert evidence.hsv_votes == {"tracked": 2}


def test_one_hsv_tracked_frame_next_to_an_opponent_label_on_other_frames_stays_unknown() -> None:
    labels = ["Blue team", "Player", "Player"]
    frames = {0: _frame(0, WHITE), 1: _frame(1, None), 2: _frame(2, None)}
    tracklet = Tracklet(1, [_observation(i, label) for i, label in enumerate(labels)])

    evidence = TeamClassifier().classify(tracklet, EVENT_TIME, frames, _spec())

    assert evidence.team_class is TeamClass.UNKNOWN
    assert evidence.tracked_share == pytest.approx(0.65)


def test_many_opponent_labels_with_one_tracked_hsv_frame_remain_opponent() -> None:
    labels = ["Blue team"] * 3
    frames = {0: _frame(0, WHITE), 1: _frame(1, None), 2: _frame(2, None)}
    tracklet = Tracklet(1, [_observation(i, label) for i, label in enumerate(labels)])

    evidence = TeamClassifier().classify(tracklet, EVENT_TIME, frames, _spec())

    assert evidence.team_class is TeamClass.OPPONENT


def test_guard_does_not_apply_when_the_detector_also_voted_tracked() -> None:
    evidence = _classify(["Blue team", "White team"], colors=[WHITE, WHITE])

    assert evidence.team_class is TeamClass.TRACKED


def test_hsv_opponent_still_overrules_a_tracked_label_even_for_a_single_frame() -> None:
    evidence = _classify(["White team"], colors=[BLUE])

    assert evidence.team_class is TeamClass.OPPONENT
    assert evidence.confidence == pytest.approx(0.65)


def test_label_only_opponent_is_unaffected_by_the_guard() -> None:
    evidence = TeamClassifier().classify(_tracklet(["Blue team"]), EVENT_TIME, None, _spec())

    assert evidence.team_class is TeamClass.OPPONENT
