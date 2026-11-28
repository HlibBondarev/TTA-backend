"""Tests for opponent suppression, goalkeeper evidence and the soft ball bonus in ActorMatcher."""

from __future__ import annotations

import numpy as np
import pytest

from tests.support import make_candidate, make_context
from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass2.actor_matcher import (
    EXCLUDED_NOT_GOALKEEPER,
    EXCLUDED_OPPONENT,
    ActorMatcher,
)
from video_analyzer.pass2.failure_reasons import Pass2FailureReason
from video_analyzer.pass2.team_classifier import TeamClass
from video_analyzer.pass2.tracker import (
    BoTSortTracker,
    FrameDetection,
    Tracklet,
    TrackletObservation,
)

FRAME_W, FRAME_H = 400, 300
EVENT_TIME = 20.0
# Normalized hint covering pixel box (150, 90)-(250, 210).
HINT = [150 / FRAME_W, 90 / FRAME_H, 250 / FRAME_W, 210 / FRAME_H]
TARGET_BOX = (150.0, 90.0, 250.0, 210.0)


def _tracklet(
    track_id: int,
    label: str,
    box: tuple[float, float, float, float] = TARGET_BOX,
    frames: int = 1,
    start_time: float = EVENT_TIME,
) -> Tracklet:
    observations = [
        TrackletObservation(i, start_time + 0.25 * i, box, 0.9, label) for i in range(frames)
    ]
    return Tracklet(track_id, observations)


def _match(tracklets, name="Goal", is_positive=True, **kwargs):
    context = make_context()
    candidate = make_candidate(context, name, is_positive, bbox=HINT, time_sec=EVENT_TIME)
    return ActorMatcher().match_detailed(candidate, tracklets, context, FRAME_W, FRAME_H, **kwargs)


def _breakdown(result, track_id):
    return next(b for b in result.breakdowns if b.track_id == track_id)


# --------------------------------------------------------------- opponent hard exclusion


def test_opponent_tracklet_is_hard_excluded_even_with_the_better_spatial_overlap() -> None:
    exact_opponent = _tracklet(1, "Blue team", TARGET_BOX)
    shifted_tracked = _tracklet(2, "White team", (170.0, 100.0, 270.0, 220.0))

    result = _match([exact_opponent, shifted_tracked])

    assert result.matched is not None
    assert result.matched.tracklet.track_id == 2
    assert result.matched.team_class is TeamClass.TRACKED
    assert _breakdown(result, 1).exclusion_reason == EXCLUDED_OPPONENT
    assert result.opponent_exclusions == 1


def test_all_spatially_valid_tracklets_excluded_as_opponent_reports_no_tracked_team_actor() -> None:
    result = _match([_tracklet(1, "Blue team"), _tracklet(2, "Blue team", frames=3)])

    assert result.matched is None
    assert result.failure_reason is Pass2FailureReason.NO_TRACKED_TEAM_ACTOR
    assert result.opponent_exclusions == 2


def test_match_wrapper_never_falls_back_to_an_opponent() -> None:
    context = make_context()
    candidate = make_candidate(context, bbox=HINT, time_sec=EVENT_TIME)

    matched = ActorMatcher().match(
        candidate, [_tracklet(1, "Blue team")], context, FRAME_W, FRAME_H
    )

    assert matched is None


def test_spatially_invalid_opponent_does_not_trigger_no_tracked_team_actor() -> None:
    far_opponent = _tracklet(1, "Blue team", (0.0, 0.0, 20.0, 20.0))

    result = _match([far_opponent])

    assert result.matched is None
    assert result.failure_reason is Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED
    assert result.opponent_exclusions == 0


def test_unknown_team_tracklet_stays_eligible_but_is_flagged_unknown() -> None:
    result = _match([_tracklet(1, "Player")])

    assert result.matched is not None
    assert result.matched.team_class is TeamClass.UNKNOWN
    assert result.failure_reason is None


def test_tracked_team_evidence_outranks_unknown_with_the_same_geometry() -> None:
    result = _match([_tracklet(1, "Player"), _tracklet(2, "White team")])

    assert result.matched is not None
    assert result.matched.tracklet.track_id == 2
    assert _breakdown(result, 2).team_bonus == pytest.approx(0.15)
    assert _breakdown(result, 1).team_bonus == 0.0


def test_exact_ties_are_still_reported() -> None:
    result = _match([_tracklet(1, "White team"), _tracklet(2, "White team")])

    assert result.matched is not None
    assert result.matched.is_tied is True


# ------------------------------------------------------------------- goalkeeper evidence


def test_goalkeeper_event_requires_goalkeeper_evidence() -> None:
    field_player = _tracklet(1, "White team", TARGET_BOX)
    goalkeeper = _tracklet(2, "White Goalkeeper", (170.0, 100.0, 270.0, 220.0))

    result = _match([field_player, goalkeeper], name="Goal", is_positive=False)

    assert result.matched is not None
    assert result.matched.tracklet.track_id == 2
    assert _breakdown(result, 1).exclusion_reason == EXCLUDED_NOT_GOALKEEPER
    assert _breakdown(result, 2).team_bonus == pytest.approx(0.35)


def test_goalkeeper_event_without_goalkeeper_evidence_fails_without_tracked_team_reason() -> None:
    result = _match([_tracklet(1, "White team")], name="Goal", is_positive=False)

    assert result.matched is None
    assert result.failure_reason is Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED


def test_pure_goalkeeper_gets_a_soft_penalty_on_field_events_but_stays_eligible() -> None:
    goalkeeper_only = _match([_tracklet(1, "Goalkeeper")])
    competing = _match([_tracklet(1, "Goalkeeper"), _tracklet(2, "Player")])

    assert goalkeeper_only.matched is not None
    assert _breakdown(goalkeeper_only, 1).team_bonus == pytest.approx(-0.10)
    assert competing.matched is not None
    assert competing.matched.tracklet.track_id == 2


def test_red_cap_hsv_satisfies_goalkeeper_evidence_for_unlabeled_players() -> None:
    image = np.full((FRAME_H, FRAME_W, 3), 100, dtype=np.uint8)
    image[90:130, 160:240] = (20, 20, 220)
    frame = ExtractedFrame(index=0, video_time_sec=EVENT_TIME, image_bgr=image)

    result = _match([_tracklet(1, "Player")], name="Goal", is_positive=False, frames=[frame])

    assert result.matched is not None
    assert result.matched.team_evidence is not None
    assert result.matched.team_evidence.goalkeeper_evidence is True


def test_blue_cap_hsv_excludes_an_unlabeled_opponent() -> None:
    image = np.full((FRAME_H, FRAME_W, 3), 100, dtype=np.uint8)
    image[90:130, 160:240] = (200, 40, 10)
    frame = ExtractedFrame(index=0, video_time_sec=EVENT_TIME, image_bgr=image)

    result = _match([_tracklet(1, "Player")], frames=[frame])

    assert result.matched is None
    assert result.failure_reason is Pass2FailureReason.NO_TRACKED_TEAM_ACTOR


# ----------------------------------------------------------------------- ball bonus (D4)


def _ball(frame_index: int, time_sec: float, center: tuple[float, float]) -> FrameDetection:
    cx, cy = center
    return FrameDetection(
        frame_index=frame_index,
        video_time_sec=time_sec,
        bbox_xyxy=(cx - 3.0, cy - 3.0, cx + 3.0, cy + 3.0),
        confidence=0.8,
        raw_class_name="ball",
        role="ball",
    )


def _two_players() -> list[Tracklet]:
    near_ball = _tracklet(1, "White team", TARGET_BOX, frames=4, start_time=EVENT_TIME - 0.25)
    other = _tracklet(
        2, "White team", (155.0, 95.0, 255.0, 215.0), frames=4, start_time=EVENT_TIME - 0.25
    )
    return [near_ball, other]


def test_ball_bonus_is_zero_with_fewer_than_three_detections_in_window() -> None:
    balls = [_ball(0, 19.75, (200.0, 100.0)), _ball(1, 20.0, (200.0, 100.0))]

    result = _match(_two_players(), ball_detections=balls)

    assert all(b.ball_bonus == 0.0 for b in result.breakdowns)
    assert result.ball_detections_in_window == 2


def test_ball_bonus_ignores_detections_outside_the_asymmetric_window() -> None:
    balls = [
        _ball(0, EVENT_TIME - 1.5, (200.0, 100.0)),
        _ball(1, EVENT_TIME - 1.2, (200.0, 100.0)),
        _ball(2, EVENT_TIME + 0.75, (200.0, 100.0)),
        _ball(3, EVENT_TIME + 0.5, (200.0, 100.0)),
    ]

    result = _match(_two_players(), ball_detections=balls)

    assert result.ball_detections_in_window == 1
    assert all(b.ball_bonus == 0.0 for b in result.breakdowns)


def test_ball_bonus_rewards_the_tracklet_closest_to_the_ball_and_is_capped() -> None:
    # Ball sits inside the water-polo head zone of tracklet 1 (y 90..180).
    balls = [_ball(i, EVENT_TIME - 0.25 + 0.25 * i, (200.0, 110.0)) for i in range(4)]
    # Move tracklet 2 further down so its head zone is far from the ball.
    lower_box = (150.0, 150.0, 250.0, 270.0)
    players = [
        _tracklet(1, "White team", TARGET_BOX, frames=4, start_time=EVENT_TIME - 0.25),
        _tracklet(2, "White team", lower_box, frames=4, start_time=EVENT_TIME - 0.25),
    ]

    result = _match(players, ball_detections=balls)

    first, second = _breakdown(result, 1), _breakdown(result, 2)
    assert first.ball_bonus == pytest.approx(0.10)
    assert 0.0 <= second.ball_bonus < first.ball_bonus
    assert result.matched is not None
    assert result.matched.tracklet.track_id == 1


def test_ball_evidence_never_filters_out_a_tracklet() -> None:
    balls = [_ball(i, EVENT_TIME - 0.25 + 0.25 * i, (900.0, 900.0)) for i in range(4)]

    result = _match(_two_players(), ball_detections=balls)

    assert all(b.exclusion_reason is None for b in result.breakdowns)
    assert all(b.final_score is not None for b in result.breakdowns)
    assert all(b.ball_bonus == 0.0 for b in result.breakdowns)


def test_missing_ball_detections_leave_ranking_unchanged() -> None:
    without = _match(_two_players())
    with_none = _match(_two_players(), ball_detections=[])

    assert [b.final_score for b in without.breakdowns] == [
        b.final_score for b in with_none.breakdowns
    ]


# -------------------------------------------------------------------- tracker integration


class _ConfidenceDetector(BaseObjectDetector):
    def __init__(self, confidence: float) -> None:
        self._confidence = confidence

    def provider_info(self) -> dict[str, str]:
        return {"provider": "synthetic", "model": "test"}

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        return [
            Detection(
                bbox_xyxy=(100.0, 100.0, 180.0, 220.0),
                confidence=self._confidence,
                role=CanonicalRole.PLAYER,
                raw_class_name="White Goalkeeper",
            )
        ]


def _frames(count: int = 3) -> list[ExtractedFrame]:
    return [
        ExtractedFrame(i, 10.0 + 0.25 * i, np.zeros((300, 400, 3), dtype=np.uint8))
        for i in range(count)
    ]


@pytest.mark.parametrize("confidence", [0.30, 0.38, 0.44])
def test_tracker_births_tracks_from_low_confidence_detections(confidence: float) -> None:
    tracklets = BoTSortTracker().track_window(_frames(), _ConfidenceDetector(confidence))

    assert len(tracklets) == 1
    assert len(tracklets[0].observations) == 3


def test_tracker_does_not_birth_tracks_below_birth_confidence() -> None:
    assert BoTSortTracker().track_window(_frames(), _ConfidenceDetector(0.29)) == []


def test_tracker_birth_confidence_is_configurable() -> None:
    detector = _ConfidenceDetector(0.40)

    assert BoTSortTracker(track_birth_conf=0.45).track_window(_frames(), detector) == []
    assert len(BoTSortTracker(track_birth_conf=0.35).track_window(_frames(), detector)) == 1


def test_tracklet_label_votes_are_normalized_and_windowed() -> None:
    tracklet = Tracklet(
        1,
        [
            TrackletObservation(0, 10.0, TARGET_BOX, 0.9, " White Team "),
            TrackletObservation(1, 10.5, TARGET_BOX, 0.9, "white team"),
            TrackletObservation(2, 14.0, TARGET_BOX, 0.9, "Blue team"),
        ],
    )

    assert tracklet.label_votes() == {"white team": 2, "blue team": 1}
    assert tracklet.label_votes(10.0, 1.0) == {"white team": 2}


def test_tracker_records_every_detection_for_debug_overlays() -> None:
    result = BoTSortTracker().track_window_with_scene(_frames(2), _ConfidenceDetector(0.9))

    assert len(result.all_detections) == 2
    assert {det.role for det in result.all_detections} == {"player"}


# ------------------------------------------------------------- one ball per frame


class _BallsDetector(BaseObjectDetector):
    """One tracked player plus several ball candidates (real ball, cone and fence artifacts)."""

    def __init__(self, balls: list[tuple[float, tuple[float, float, float, float]]]) -> None:
        self._balls = balls

    def provider_info(self) -> dict[str, str]:
        return {"provider": "synthetic", "model": "test"}

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        player = Detection(TARGET_BOX, 0.9, CanonicalRole.PLAYER, "White team")
        balls = [Detection(box, conf, CanonicalRole.BALL, "ball") for conf, box in self._balls]
        return [player, *balls]


REAL_BALL = (0.98, (370.0, 10.0, 380.0, 20.0))
CONE_BALL = (0.55, (190.0, 100.0, 210.0, 120.0))  # sits on the player's head
FENCE_BALL = (0.36, (195.0, 95.0, 205.0, 105.0))  # also on the player's head
WINDOW_TIMES = [EVENT_TIME - 0.25 + 0.25 * i for i in range(4)]


def _window_frames() -> list[ExtractedFrame]:
    return [
        ExtractedFrame(i, t, np.zeros((FRAME_H, FRAME_W, 3), dtype=np.uint8))
        for i, t in enumerate(WINDOW_TIMES)
    ]


def test_tracker_keeps_only_the_most_confident_ball_per_frame() -> None:
    detector = _BallsDetector([CONE_BALL, REAL_BALL, FENCE_BALL])

    result = BoTSortTracker().track_window_with_scene(_window_frames(), detector)

    assert len(result.ball_detections) == len(WINDOW_TIMES)
    assert {det.confidence for det in result.ball_detections} == {0.98}
    assert sorted({det.frame_index for det in result.ball_detections}) == [0, 1, 2, 3]


def test_ball_choice_does_not_depend_on_detection_order() -> None:
    forward = _BallsDetector([REAL_BALL, CONE_BALL, FENCE_BALL])
    backward = _BallsDetector([FENCE_BALL, CONE_BALL, REAL_BALL])

    first = BoTSortTracker().track_window_with_scene(_window_frames(), forward)
    second = BoTSortTracker().track_window_with_scene(_window_frames(), backward)

    assert [d.confidence for d in first.ball_detections] == [0.98] * 4
    assert [d.confidence for d in second.ball_detections] == [0.98] * 4


def test_debug_detections_stay_honest_about_suppressed_balls() -> None:
    detector = _BallsDetector([CONE_BALL, REAL_BALL, FENCE_BALL])

    result = BoTSortTracker().track_window_with_scene(_window_frames(), detector)

    balls = [d for d in result.all_detections if d.role == "ball"]
    assert len(balls) == 3 * len(WINDOW_TIMES)
    assert {d.confidence for d in balls if d.suppressed} == {0.55, 0.36}
    assert all(not d.suppressed for d in balls if d.confidence == 0.98)
    assert all(not d.suppressed for d in result.all_detections if d.role == "player")


def test_max_balls_per_frame_is_configurable() -> None:
    detector = _BallsDetector([CONE_BALL, REAL_BALL, FENCE_BALL])

    two = BoTSortTracker().track_window_with_scene(
        _window_frames(), detector, max_balls_per_frame=2
    )
    none = BoTSortTracker().track_window_with_scene(
        _window_frames(), detector, max_balls_per_frame=0
    )

    assert sorted({d.confidence for d in two.ball_detections}) == [0.55, 0.98]
    assert len(two.ball_detections) == 2 * len(WINDOW_TIMES)
    assert none.ball_detections == []


def test_profiles_keep_one_ball_per_frame_by_default() -> None:
    from video_analyzer.sport import DefaultProfile, WaterPoloProfile

    assert DefaultProfile().max_balls_per_frame == 1
    assert WaterPoloProfile().max_balls_per_frame == 1


def test_ball_proximity_scoring_ignores_suppressed_decoy_balls() -> None:
    detector = _BallsDetector([CONE_BALL, REAL_BALL, FENCE_BALL])
    frames = _window_frames()
    tracker = BoTSortTracker()

    filtered = tracker.track_window_with_scene(frames, detector)
    unfiltered = tracker.track_window_with_scene(frames, detector, max_balls_per_frame=3)

    kept = _match(filtered.tracklets, ball_detections=filtered.ball_detections)
    inflated = _match(unfiltered.tracklets, ball_detections=unfiltered.ball_detections)

    # With one ball per frame the real (far) ball is the only evidence: no false bonus.
    assert kept.ball_detections_in_window == len(WINDOW_TIMES)
    assert kept.breakdowns[0].ball_bonus == 0.0
    # Without the filter the decoys sitting on the head would have granted the full bonus.
    assert inflated.ball_detections_in_window == 3 * len(WINDOW_TIMES)
    assert inflated.breakdowns[0].ball_bonus == pytest.approx(0.10)


def test_detection_overlay_marks_suppressed_balls_and_is_written(tmp_path) -> None:
    from video_analyzer.pass2.debug_artifacts import write_detection_overlay

    frames = _window_frames()
    result = BoTSortTracker().track_window_with_scene(
        frames, _BallsDetector([CONE_BALL, REAL_BALL])
    )

    path = write_detection_overlay(tmp_path, 1, frames[1], result, HINT, TARGET_BOX)

    assert path.is_file()
    assert path.name == "event_001_all_detections.jpg"


def test_unknown_class_from_thin_hsv_evidence_gets_no_team_bonus() -> None:
    image = np.full((FRAME_H, FRAME_W, 3), 100, dtype=np.uint8)
    image[96:150, 175:225] = (255, 255, 255)  # white cap color in the color zone
    frame = ExtractedFrame(index=0, video_time_sec=EVENT_TIME, image_bgr=image)

    result = _match([_tracklet(1, "Blue team")], frames=[frame])

    assert result.matched is not None
    assert result.matched.team_class is TeamClass.UNKNOWN
    assert _breakdown(result, 1).team_bonus == 0.0


# ---------------------------------- goalkeeper role without team identity (review finding)


def _red_zone_frame() -> ExtractedFrame:
    image = np.full((FRAME_H, FRAME_W, 3), 100, dtype=np.uint8)
    image[96:150, 175:225] = (20, 20, 220)  # solid red cap in the color zone
    return ExtractedFrame(index=0, video_time_sec=EVENT_TIME, image_bgr=image)


def test_solid_red_goalkeeper_is_unknown_but_eligible_and_bonused_on_goalkeeper_events() -> None:
    result = _match(
        [_tracklet(1, "Player")], name="Goal", is_positive=False, frames=[_red_zone_frame()]
    )

    assert result.matched is not None
    assert result.matched.team_class is TeamClass.UNKNOWN
    assert result.matched.team_evidence is not None
    assert result.matched.team_evidence.goalkeeper_evidence is True
    assert _breakdown(result, 1).team_bonus == pytest.approx(0.20)
    assert _breakdown(result, 1).exclusion_reason is None


def test_generic_goalkeeper_label_is_eligible_on_goalkeeper_events() -> None:
    result = _match([_tracklet(1, "Goalkeeper")], name="Goal", is_positive=False)

    assert result.matched is not None
    assert result.matched.team_class is TeamClass.UNKNOWN
    assert _breakdown(result, 1).team_bonus == pytest.approx(0.20)


def test_solid_red_goalkeeper_gets_the_soft_penalty_on_field_events_but_is_never_excluded() -> None:
    result = _match([_tracklet(1, "Player")], frames=[_red_zone_frame()])

    assert result.matched is not None
    assert _breakdown(result, 1).team_bonus == pytest.approx(-0.10)
    assert _breakdown(result, 1).exclusion_reason is None


def test_opponent_label_with_red_cap_color_is_still_hard_excluded_on_goalkeeper_events() -> None:
    result = _match(
        [_tracklet(1, "Blue team")], name="Goal", is_positive=False, frames=[_red_zone_frame()]
    )

    assert result.matched is None
    assert _breakdown(result, 1).exclusion_reason == EXCLUDED_OPPONENT
    assert result.failure_reason is Pass2FailureReason.NO_TRACKED_TEAM_ACTOR


# ----------------------------------------- ball telemetry and unhinted ranking (review findings)


def test_ball_detections_in_window_is_reported_even_without_any_tracklet() -> None:
    balls = [
        _ball(0, EVENT_TIME - 0.5, (200.0, 100.0)),
        _ball(1, EVENT_TIME, (200.0, 100.0)),
        _ball(2, EVENT_TIME + 0.4, (200.0, 100.0)),
        _ball(3, EVENT_TIME - 3.0, (200.0, 100.0)),
        _ball(4, EVENT_TIME + 2.0, (200.0, 100.0)),
    ]

    result = _match([], ball_detections=balls)

    assert result.matched is None
    assert result.failure_reason is Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED
    assert result.ball_detections_in_window == 3
    assert result.breakdowns == []


def test_ball_detections_in_window_is_zero_without_tracklets_or_balls() -> None:
    assert _match([]).ball_detections_in_window == 0
    assert _match([], ball_detections=[]).ball_detections_in_window == 0


def _match_unhinted(tracklets, **kwargs):
    context = make_context()
    candidate = make_candidate(context, bbox=HINT, time_sec=EVENT_TIME)
    candidate.actor_bbox_normalized = None
    return ActorMatcher().match_detailed(candidate, tracklets, context, FRAME_W, FRAME_H, **kwargs)


def test_ball_proximity_improves_unhinted_candidate_ranking() -> None:
    box_a = (20.0, 90.0, 120.0, 210.0)
    box_b = (270.0, 90.0, 370.0, 210.0)
    start = EVENT_TIME - 0.25
    tracklets = [
        _tracklet(1, "White team", box_a, frames=4, start_time=start),
        _tracklet(2, "White team", box_b, frames=4, start_time=start),
    ]
    balls = [_ball(i, start + 0.25 * i, (320.0, 110.0)) for i in range(4)]

    without = _match_unhinted(tracklets)
    with_ball = _match_unhinted(tracklets, ball_detections=balls)

    assert without.matched is not None
    assert without.matched.is_tied is True  # identical evidence: only the ball can break it
    assert with_ball.matched is not None
    assert with_ball.matched.tracklet.track_id == 2
    assert with_ball.matched.is_tied is False
    assert with_ball.matched.has_spatial_hint is False
    assert _breakdown(with_ball, 2).ball_bonus == pytest.approx(0.10)
    assert _breakdown(with_ball, 1).ball_bonus == 0.0
    assert _breakdown(with_ball, 2).final_score > _breakdown(with_ball, 1).final_score


def test_unhinted_matches_still_report_no_spatial_hint_for_the_downstream_gate() -> None:
    result = _match_unhinted([_tracklet(1, "White team")])

    assert result.matched is not None
    assert result.matched.has_spatial_hint is False
