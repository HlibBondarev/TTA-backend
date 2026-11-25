"""Tests for BoT-SORT panning tracklet association, crop sharpness selection, and OCR."""

from __future__ import annotations

from types import SimpleNamespace

import cv2
import numpy as np
import pytest

from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.actor_matcher import ActorMatcher
from video_analyzer.pass2.cap_resolver import GeminiCropGridCapResolver, LocalOcrCapResolver
from video_analyzer.pass2.crop_selector import CropCandidate, CropSelector, SelectedCrops
from video_analyzer.pass2.lineup_matcher import LineupMatcher
from video_analyzer.pass2.tracker import (
    BoTSortTracker,
    Tracklet,
    TrackletObservation,
    _scale_warp_translation,
    _TrackState,
    _warp_bbox,
)
from video_analyzer.schemas.request import MatchSnapshot


class SyntheticDetector(BaseObjectDetector):
    """Emits detections corresponding to textured rectangles drawn on synthetic frames."""

    def __init__(self, frame_boxes: dict[int, list[Detection]]) -> None:
        self._frame_boxes = frame_boxes
        self._idx = 0

    def provider_info(self) -> dict[str, str]:
        return {"provider": "synthetic", "model": "test"}

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        dets = self._frame_boxes.get(self._idx, [])
        self._idx += 1
        return dets


def test_botsort_tracker_maintains_track_across_panning_frames() -> None:
    frames: list[ExtractedFrame] = []
    frame_boxes: dict[int, list[Detection]] = {}

    base_bg = np.zeros((360, 640, 3), dtype=np.uint8)
    rng = np.random.default_rng(42)
    for _ in range(80):
        cx = int(rng.integers(20, 600))
        cy = int(rng.integers(20, 340))
        cv2.circle(base_bg, (cx, cy), 4, (200, 200, 200), -1)

    for idx in range(5):
        shift_x = idx * 35  # strong horizontal camera pan
        mat = np.float32([[1, 0, -shift_x], [0, 1, 0]])
        panned = cv2.warpAffine(base_bg, mat, (640, 360))
        x1 = 420.0 - shift_x
        cv2.putText(
            panned,
            "7",
            (int(x1 + 20), 140),
            cv2.FONT_HERSHEY_SIMPLEX,
            1.4,
            (255, 255, 255),
            3,
        )
        frames.append(ExtractedFrame(index=idx, video_time_sec=10.0 + idx * 0.25, image_bgr=panned))
        frame_boxes[idx] = [
            Detection(
                bbox_xyxy=(x1, 100.0, x1 + 80.0, 220.0),
                confidence=0.9,
                role=CanonicalRole.PLAYER,
                raw_class_name="White team",
            )
        ]

    tracker = BoTSortTracker(iou_threshold=0.20)
    tracklets = tracker.track_window(frames, SyntheticDetector(frame_boxes))

    assert len(tracklets) == 1
    assert len(tracklets[0].observations) == 5


def test_actor_matcher_and_crop_selector_prefer_sharp_frames(
    sample_snapshot: MatchSnapshot,
) -> None:
    # Build two frames: frame 0 sharp, frame 1 heavily blurred
    sharp = np.zeros((300, 400, 3), dtype=np.uint8)
    cv2.putText(sharp, "12", (175, 110), cv2.FONT_HERSHEY_SIMPLEX, 1.5, (255, 255, 255), 3)
    blurred = cv2.GaussianBlur(sharp, (25, 25), 10.0)

    frames = [
        ExtractedFrame(index=0, video_time_sec=20.0, image_bgr=sharp),
        ExtractedFrame(index=1, video_time_sec=20.2, image_bgr=blurred),
    ]
    boxes = {
        0: [
            Detection((150.0, 60.0, 240.0, 220.0), 0.9, CanonicalRole.PLAYER, "White team"),
        ],
        1: [
            Detection((150.0, 60.0, 240.0, 220.0), 0.9, CanonicalRole.PLAYER, "White team"),
        ],
    }

    tracklets = BoTSortTracker().track_window(frames, SyntheticDetector(boxes))
    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=20.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=[150 / 400, 60 / 300, 240 / 400, 220 / 300],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.9,
    )

    matched = ActorMatcher().match(
        cand, tracklets, sample_snapshot, frame_width=400, frame_height=300
    )
    assert matched is not None

    selected = CropSelector(min_frame_gap=1).select_and_build_sheet(
        matched.tracklet, frames, max_crops=1
    )
    assert selected is not None
    assert selected.crops[0].frame_index == 0  # picked sharp frame over blurred frame
    assert len(selected.contact_sheet_jpeg) > 100


def test_cap_resolvers_constrain_to_roster() -> None:
    crop = np.zeros((64, 64, 3), dtype=np.uint8)
    from video_analyzer.pass2.crop_selector import CropCandidate, SelectedCrops

    selected = SelectedCrops(
        crops=[CropCandidate(0, 10.0, crop, 100.0)],
        contact_sheet_jpeg=b"jpeg-bytes",
        contact_sheet_bgr=crop,
    )

    class FakeGeminiClient:
        class models:
            @staticmethod
            def generate_content(**kwargs):
                return SimpleNamespace(
                    text='{"tile_readings":[{"tile_index":1,"cap_number":7,"confidence":0.93}],'
                    '"consensus_cap_number":7,"confidence":0.93,'
                    '"candidate_numbers":[{"cap_number":7,"score":0.93}],'
                    '"reasoning":"Clear white cap #7"}'
                )

    gemini_resolver = GeminiCropGridCapResolver(api_key="test", client=FakeGeminiClient())
    res = gemini_resolver.resolve(selected, allowed_cap_numbers=[1, 7, 9], cap_color_hint="white")
    assert res.resolved_cap_number == 7
    assert res.confidence == 0.93

    # Local OCR fallback test
    class FakeOcrReader:
        @staticmethod
        def readtext(img, allowlist=None):
            return [([[0, 0], [10, 0], [10, 10], [0, 10]], "9", 0.88)]

    ocr_resolver = LocalOcrCapResolver(reader=FakeOcrReader())
    ocr_res = ocr_resolver.resolve(selected, allowed_cap_numbers=[1, 7, 9])
    assert ocr_res.resolved_cap_number == 9
    assert ocr_res.confidence > 0.8


def test_actor_matcher_rejects_spatially_incompatible_tracklet_despite_label_bonuses(
    sample_snapshot: MatchSnapshot,
) -> None:
    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "ASV")
    cand = Pass1EventCandidate(
        video_time_sec=20.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=False,
        actor_bbox_normalized=[0.05, 0.05, 0.15, 0.20],
        pool_zone_hint="away_goal",
        cap_color_hint="white",
        confidence=0.9,
    )
    far_tracklet = Tracklet(
        track_id=1,
        observations=[
            TrackletObservation(
                frame_index=0,
                video_time_sec=20.0,
                bbox_xyxy=(800.0, 750.0, 950.0, 950.0),
                confidence=0.95,
                raw_class_name="White Goalkeeper",
            )
        ],
    )

    matched = ActorMatcher().match(
        cand, [far_tracklet], sample_snapshot, frame_width=1000, frame_height=1000
    )
    assert matched is None


def test_actor_matcher_marks_unhinted_match_without_spatial_hint(
    sample_snapshot: MatchSnapshot,
) -> None:
    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=20.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=None,
        pool_zone_hint=None,
        cap_color_hint="white",
        confidence=0.9,
    )
    tracklet = Tracklet(
        track_id=1,
        observations=[
            TrackletObservation(
                frame_index=0,
                video_time_sec=20.0,
                bbox_xyxy=(100.0, 100.0, 180.0, 220.0),
                confidence=0.92,
                raw_class_name="White team",
            )
        ],
    )

    matched = ActorMatcher().match(
        cand, [tracklet], sample_snapshot, frame_width=400, frame_height=300
    )
    assert matched is not None
    assert matched.has_spatial_hint is False


def test_cap_resolvers_leave_tied_top_scores_unresolved_and_cap_votes_per_crop() -> None:
    crop = np.zeros((64, 64, 3), dtype=np.uint8)
    two_crops = SelectedCrops(
        crops=[
            CropCandidate(0, 10.0, crop, 100.0),
            CropCandidate(1, 10.2, crop, 95.0),
        ],
        contact_sheet_jpeg=b"jpeg-bytes",
        contact_sheet_bgr=crop,
    )

    class TiedOcrReader:
        def __init__(self) -> None:
            self._idx = 0

        def readtext(self, img, allowlist=None):
            idx = self._idx
            self._idx += 1
            return [([[0, 0], [10, 0], [10, 10], [0, 10]], "7" if idx == 0 else "9", 0.85)]

    tied_ocr = LocalOcrCapResolver(reader=TiedOcrReader()).resolve(
        two_crops, allowed_cap_numbers=[1, 7, 9]
    )
    assert tied_ocr.resolved_cap_number is None
    assert tied_ocr.candidate_scores[7] == tied_ocr.candidate_scores[9]
    assert "tied" in tied_ocr.reasoning.lower()

    class TiedGeminiClient:
        class models:
            @staticmethod
            def generate_content(**kwargs):
                return SimpleNamespace(
                    text='{"tile_readings":[],"consensus_cap_number":7,"confidence":0.85,'
                    '"candidate_numbers":[{"cap_number":7,"score":0.85},'
                    '{"cap_number":9,"score":0.85}],"reasoning":"Ambiguous 7 vs 9"}'
                )

    tied_gemini = GeminiCropGridCapResolver(api_key="test", client=TiedGeminiClient()).resolve(
        two_crops, allowed_cap_numbers=[1, 7, 9]
    )
    assert tied_gemini.resolved_cap_number is None
    assert tied_gemini.candidate_scores[7] == pytest.approx(0.85)
    assert tied_gemini.candidate_scores[9] == pytest.approx(0.85)

    three_crops = SelectedCrops(
        crops=[
            CropCandidate(0, 10.0, crop, 100.0),
            CropCandidate(1, 10.2, crop, 95.0),
            CropCandidate(2, 10.4, crop, 90.0),
        ],
        contact_sheet_jpeg=b"jpeg-bytes",
        contact_sheet_bgr=crop,
    )

    class RepeatedSingleCropOcrReader:
        def __init__(self) -> None:
            self._idx = 0

        def readtext(self, img, allowlist=None):
            idx = self._idx
            self._idx += 1
            if idx == 0:
                # Multiple detections of #7 within one crop must yield only 1 vote
                return [
                    ([[0, 0], [5, 0], [5, 5], [0, 5]], "7", 0.90),
                    ([[5, 0], [10, 0], [10, 5], [5, 5]], "7", 0.88),
                    ([[10, 0], [15, 0], [15, 5], [10, 5]], "7", 0.85),
                ]
            return [([[0, 0], [10, 0], [10, 10], [0, 10]], "9", 0.90)]

    capped_res = LocalOcrCapResolver(reader=RepeatedSingleCropOcrReader()).resolve(
        three_crops, allowed_cap_numbers=[1, 7, 9]
    )
    assert capped_res.resolved_cap_number == 9
    assert capped_res.candidate_scores[9] > capped_res.candidate_scores[7]


def test_tracker_accumulates_missed_frames_and_keeps_relative_velocity() -> None:
    frames: list[ExtractedFrame] = []
    frame_boxes: dict[int, list[Detection]] = {}

    base_bg = np.zeros((360, 640, 3), dtype=np.uint8)
    rng = np.random.default_rng(7)
    for _ in range(90):
        cx = int(rng.integers(20, 600))
        cy = int(rng.integers(20, 340))
        cv2.circle(base_bg, (cx, cy), 4, (200, 200, 200), -1)

    for idx in range(6):
        shift_x = idx * 35
        mat = np.float32([[1, 0, -shift_x], [0, 1, 0]])
        panned = cv2.warpAffine(base_bg, mat, (640, 360))
        frames.append(ExtractedFrame(index=idx, video_time_sec=10.0 + idx * 0.2, image_bgr=panned))
        if idx in (2, 3):
            # Two consecutive missed detections mid-pan
            frame_boxes[idx] = []
        else:
            x1 = 420.0 - shift_x
            frame_boxes[idx] = [
                Detection(
                    bbox_xyxy=(x1, 100.0, x1 + 80.0, 220.0),
                    confidence=0.9,
                    role=CanonicalRole.PLAYER,
                    raw_class_name="White team",
                )
            ]

    tracker = BoTSortTracker(iou_threshold=0.20, max_lost_frames=4)
    tracklets = tracker.track_window(frames, SyntheticDetector(frame_boxes))

    assert len(tracklets) == 1
    assert [obs.frame_index for obs in tracklets[0].observations] == [0, 1, 4, 5]

    # Verify _TrackState excludes camera translation from object velocity
    state = _TrackState(
        track_id=1,
        obs=TrackletObservation(0, 0.0, (400.0, 100.0, 480.0, 220.0), 0.9, "White team"),
    )
    pan_warp = np.array([[1.0, 0.0, -35.0], [0.0, 1.0, 0.0]], dtype=np.float64)
    state.predict(pan_warp)
    state.update(TrackletObservation(1, 0.2, (365.0, 100.0, 445.0, 220.0), 0.9, "White team"))
    assert state.velocity == pytest.approx((0.0, 0.0), abs=1e-6)

    state.predict(pan_warp)
    state.update(TrackletObservation(2, 0.4, (342.0, 100.0, 422.0, 220.0), 0.9, "White team"))
    assert state.velocity == pytest.approx((12.0, 0.0), abs=1e-6)


def test_warp_bbox_transforms_all_four_corners_under_rotation() -> None:
    rot_45 = cv2.getRotationMatrix2D((50.0, 50.0), 45.0, 1.0)
    wx1, wy1, wx2, wy2 = _warp_bbox((0.0, 0.0, 100.0, 100.0), rot_45)
    expected_span = 100.0 * float(np.sqrt(2.0))
    assert (wx2 - wx1) == pytest.approx(expected_span, abs=1e-3)
    assert (wy2 - wy1) == pytest.approx(expected_span, abs=1e-3)


def test_actor_matcher_preserves_low_iou_overlapping_tracklet_without_label_bonus(
    sample_snapshot: MatchSnapshot,
) -> None:
    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=20.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=[0.0, 0.0, 100.0 / 400.0, 100.0 / 300.0],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.92,
    )
    overlapping_tracklet = Tracklet(
        track_id=1,
        observations=[
            TrackletObservation(
                frame_index=0,
                video_time_sec=20.0,
                bbox_xyxy=(90.0, 0.0, 190.0, 100.0),
                confidence=0.9,
                raw_class_name="Blue team",
            )
        ],
    )
    distant_tracklet = Tracklet(
        track_id=2,
        observations=[
            TrackletObservation(
                frame_index=0,
                video_time_sec=20.0,
                bbox_xyxy=(300.0, 200.0, 390.0, 290.0),
                confidence=0.95,
                raw_class_name="White Goalkeeper",
            )
        ],
    )

    matched = ActorMatcher().match(
        cand,
        [overlapping_tracklet, distant_tracklet],
        sample_snapshot,
        frame_width=400,
        frame_height=300,
    )
    assert matched is not None
    assert matched.tracklet.track_id == 1
    assert matched.has_spatial_hint is True
    assert matched.match_score == pytest.approx(0.1269, abs=1e-3)


def test_actor_matcher_filters_ineligible_non_overlapping_track_before_ranking(
    sample_snapshot: MatchSnapshot,
) -> None:
    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=20.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=[0.0, 0.0, 100.0 / 400.0, 100.0 / 300.0],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.92,
    )
    overlapping_track = Tracklet(
        track_id=1,
        observations=[
            TrackletObservation(
                frame_index=0,
                video_time_sec=20.0,
                bbox_xyxy=(90.0, 0.0, 190.0, 100.0),
                confidence=0.90,
                raw_class_name="Blue team",
            )
        ],
    )
    # Non-overlapping track at 19s: spatial_score ~= 0.1932, time_penalty = 0.05 -> 0.1432 < 0.15
    ineligible_non_overlapping_track = Tracklet(
        track_id=2,
        observations=[
            TrackletObservation(
                frame_index=0,
                video_time_sec=19.0,
                bbox_xyxy=(101.0, 40.0, 111.0, 60.0),
                confidence=0.90,
                raw_class_name="Blue team",
            )
        ],
    )

    for ordered in (
        [overlapping_track, ineligible_non_overlapping_track],
        [ineligible_non_overlapping_track, overlapping_track],
    ):
        matched = ActorMatcher().match(
            cand,
            ordered,
            sample_snapshot,
            frame_width=400,
            frame_height=300,
        )
        assert matched is not None
        assert matched.tracklet.track_id == 1
        assert matched.has_spatial_hint is True
        assert matched.match_score == pytest.approx(0.1269, abs=1e-3)


def test_gemini_cap_resolver_rejects_contradictory_or_non_top_consensus() -> None:
    crop = np.zeros((64, 64, 3), dtype=np.uint8)
    selected = SelectedCrops(
        crops=[CropCandidate(0, 10.0, crop, 100.0)],
        contact_sheet_jpeg=b"jpeg-bytes",
        contact_sheet_bgr=crop,
    )

    class ContradictoryGeminiClient:
        class models:
            @staticmethod
            def generate_content(**kwargs):
                # Candidate #9 scored 0.95, while consensus picked #7 at 0.82
                return SimpleNamespace(
                    text='{"tile_readings":[],"consensus_cap_number":7,"confidence":0.82,'
                    '"candidate_numbers":[{"cap_number":9,"score":0.95},'
                    '{"cap_number":7,"score":0.40}],"reasoning":"Conflicting reading"}'
                )

    res = GeminiCropGridCapResolver(api_key="test", client=ContradictoryGeminiClient()).resolve(
        selected, allowed_cap_numbers=[7, 9]
    )
    assert res.resolved_cap_number is None
    assert res.candidate_scores[9] == pytest.approx(0.95)
    assert res.candidate_scores[7] == pytest.approx(0.40)


def test_cross_player_cap_ambiguity_blocks_auto_assignment(
    sample_snapshot: MatchSnapshot,
) -> None:
    img = np.zeros((300, 400, 3), dtype=np.uint8)
    frames = [
        ExtractedFrame(index=0, video_time_sec=10.0, image_bgr=img),
        ExtractedFrame(index=1, video_time_sec=10.2, image_bgr=img.copy()),
    ]
    # Frame 0: shared cap centered at (160, 100) falls inside BOTH players
    # Frame 1: player 2 contains two caps (multi-cap overlap)
    boxes = {
        0: [
            Detection((100.0, 60.0, 200.0, 220.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((140.0, 60.0, 240.0, 220.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((150.0, 85.0, 170.0, 115.0), 0.95, CanonicalRole.CAP, "cap"),
        ],
        1: [
            Detection((100.0, 60.0, 200.0, 220.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((110.0, 80.0, 130.0, 110.0), 0.95, CanonicalRole.CAP, "cap"),
            Detection((160.0, 80.0, 180.0, 110.0), 0.95, CanonicalRole.CAP, "cap"),
        ],
    }

    tracklets = BoTSortTracker(iou_threshold=0.20).track_window(frames, SyntheticDetector(boxes))
    assert len(tracklets) == 2
    assert all(
        obs.cap_ambiguous is True and obs.cap_bbox_xyxy is None for obs in tracklets[0].observations
    )
    assert tracklets[1].observations[0].cap_ambiguous is True

    selected = CropSelector(min_frame_gap=1).select_and_build_sheet(
        tracklets[0], frames, max_crops=2
    )
    assert selected is not None
    assert selected.has_ambiguous_cap is True

    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=10.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=[100 / 400, 60 / 300, 200 / 400, 220 / 300],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.94,
    )
    from video_analyzer.pass2.cap_resolver import CapResolutionResult

    outcome = LineupMatcher().resolve(
        cand,
        CapResolutionResult(
            resolved_cap_number=7,
            confidence=0.95,
            candidate_scores={7: 0.95},
            reasoning="Sharp upper-body crop",
        ),
        sample_snapshot,
        min_cap_confidence=0.80,
        cap_ambiguous=selected.has_ambiguous_cap,
    )
    assert outcome.match_lineup_id is None
    assert outcome.resolved_cap_number is None
    assert outcome.needs_review is True
    assert "AMBIGUOUS_CAP_DETECTION" in outcome.review_reasons
    assert [c.cap_number for c in outcome.candidate_lineup_ids] == [7]


def test_actor_matcher_flags_tied_tracks_and_blocks_auto_confirmation(
    sample_snapshot: MatchSnapshot,
) -> None:
    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=20.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=[0.4, 0.3, 0.5, 0.6],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.95,
    )
    t1 = Tracklet(
        track_id=1,
        observations=[TrackletObservation(0, 20.0, (160.0, 90.0, 200.0, 180.0), 0.9, "White team")],
    )
    t2 = Tracklet(
        track_id=2,
        observations=[TrackletObservation(0, 20.0, (160.0, 90.0, 200.0, 180.0), 0.9, "White team")],
    )

    matched = ActorMatcher().match(
        cand, [t1, t2], sample_snapshot, frame_width=400, frame_height=300
    )
    assert matched is not None
    assert matched.is_tied is True

    from video_analyzer.pass2.cap_resolver import CapResolutionResult

    outcome = LineupMatcher().resolve(
        cand,
        CapResolutionResult(
            resolved_cap_number=7,
            confidence=0.95,
            candidate_scores={7: 0.95},
            reasoning="Cap #7",
        ),
        sample_snapshot,
        min_cap_confidence=0.80,
        actor_match_tied=matched.is_tied,
    )
    assert outcome.match_lineup_id is None
    assert outcome.resolved_cap_number is None
    assert outcome.needs_review is True
    assert "TIED_ACTOR_TRACKLET_MATCH" in outcome.review_reasons
    assert [c.cap_number for c in outcome.candidate_lineup_ids] == [7]


def test_fallback_crop_overlapping_neighbor_cap_marks_ambiguous_and_blocks_auto_assign(
    sample_snapshot: MatchSnapshot,
) -> None:
    img = np.zeros((240, 300, 3), dtype=np.uint8)
    frames = [
        ExtractedFrame(index=0, video_time_sec=10.0, image_bgr=img),
        ExtractedFrame(index=1, video_time_sec=10.2, image_bgr=img.copy()),
    ]
    # Frame 0: shared cap centered at (75, 20) inside both players -> both ambiguous.
    # Frame 1: cap (97, 10, 105, 30) center is (101, 20), which belongs only to Player 2,
    # yet overlaps Player 1's padded upper-body fallback crop (-6, -18, 106, 108).
    boxes = {
        0: [
            Detection((0.0, 0.0, 100.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((50.0, 0.0, 150.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((70.0, 10.0, 80.0, 30.0), 0.95, CanonicalRole.CAP, "cap"),
        ],
        1: [
            Detection((0.0, 0.0, 100.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((50.0, 0.0, 150.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((97.0, 10.0, 105.0, 30.0), 0.95, CanonicalRole.CAP, "cap"),
        ],
    }

    tracklets = BoTSortTracker(iou_threshold=0.20).track_window(frames, SyntheticDetector(boxes))
    assert len(tracklets) == 2

    player1_obs1 = tracklets[0].observations[1]
    assert player1_obs1.cap_bbox_xyxy is None
    assert player1_obs1.cap_ambiguous is True

    player2_obs1 = tracklets[1].observations[1]
    assert player2_obs1.cap_bbox_xyxy == (97.0, 10.0, 105.0, 30.0)
    assert player2_obs1.cap_ambiguous is False

    selected = CropSelector(min_frame_gap=1).select_and_build_sheet(
        tracklets[0], frames, max_crops=2
    )
    assert selected is not None
    assert selected.has_ambiguous_cap is True

    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=10.2,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=[0.0, 0.0, 100.0 / 300.0, 200.0 / 240.0],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.95,
    )
    from video_analyzer.pass2.cap_resolver import CapResolutionResult

    outcome = LineupMatcher().resolve(
        cand,
        CapResolutionResult(
            resolved_cap_number=7,
            confidence=0.95,
            candidate_scores={7: 0.95},
            reasoning="Clear cap #7 inside padded crop",
        ),
        sample_snapshot,
        min_cap_confidence=0.80,
        cap_ambiguous=selected.has_ambiguous_cap,
    )
    assert outcome.match_lineup_id is None
    assert outcome.resolved_cap_number is None
    assert outcome.needs_review is True
    assert "AMBIGUOUS_CAP_DETECTION" in outcome.review_reasons

    # Without cap detections, overlapping head zones are flagged while separated players stay clean
    overlap_no_caps = BoTSortTracker().track_window(
        [frames[0]],
        SyntheticDetector(
            {
                0: [
                    Detection((0.0, 0.0, 100.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
                    Detection((50.0, 0.0, 150.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
                ]
            }
        ),
    )
    assert all(t.observations[0].cap_ambiguous is True for t in overlap_no_caps)

    separated_no_caps = BoTSortTracker().track_window(
        [frames[0]],
        SyntheticDetector(
            {
                0: [
                    Detection((0.0, 0.0, 100.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
                    Detection((160.0, 0.0, 260.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
                ]
            }
        ),
    )
    assert all(t.observations[0].cap_ambiguous is False for t in separated_no_caps)


def test_unique_cap_crop_overlapping_neighbor_unowned_head_marks_ambiguous(
    sample_snapshot: MatchSnapshot,
) -> None:
    img = np.zeros((240, 300, 3), dtype=np.uint8)
    frames = [ExtractedFrame(index=0, video_time_sec=10.0, image_bgr=img)]

    # Cap (70, 10, 90, 30) center is (80, 20), inside Player 1 (0, 0, 100, 200) only.
    # Its padded crop (66, 6, 94, 34) overlaps Player 2's unowned head zone (86, 0, 126, 90).
    boxes = {
        0: [
            Detection((0.0, 0.0, 100.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((81.0, 0.0, 131.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((70.0, 10.0, 90.0, 30.0), 0.95, CanonicalRole.CAP, "cap"),
        ]
    }
    tracklets = BoTSortTracker().track_window(frames, SyntheticDetector(boxes))
    assert len(tracklets) == 2

    player1_obs = tracklets[0].observations[0]
    assert player1_obs.cap_bbox_xyxy == (70.0, 10.0, 90.0, 30.0)
    assert player1_obs.cap_ambiguous is True

    selected = CropSelector(min_frame_gap=1).select_and_build_sheet(
        tracklets[0], frames, max_crops=1
    )
    assert selected is not None
    assert selected.has_ambiguous_cap is True

    defn = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    cand = Pass1EventCandidate(
        video_time_sec=10.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=True,
        actor_bbox_normalized=[0.0, 0.0, 100.0 / 300.0, 200.0 / 240.0],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.95,
    )
    from video_analyzer.pass2.cap_resolver import CapResolutionResult

    outcome = LineupMatcher().resolve(
        cand,
        CapResolutionResult(
            resolved_cap_number=7,
            confidence=0.95,
            candidate_scores={7: 0.95},
            reasoning="Clear cap #7 in crop overlapping neighbor head",
        ),
        sample_snapshot,
        min_cap_confidence=0.80,
        cap_ambiguous=selected.has_ambiguous_cap,
    )
    assert outcome.match_lineup_id is None
    assert outcome.resolved_cap_number is None
    assert outcome.needs_review is True
    assert "AMBIGUOUS_CAP_DETECTION" in outcome.review_reasons

    # Positive contrast: when Player 2's cap is detected outside Player 1's padded crop,
    # both players remain unambiguous.
    clean_boxes = {
        0: [
            Detection((0.0, 0.0, 100.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((81.0, 0.0, 131.0, 200.0), 0.9, CanonicalRole.PLAYER, "White team"),
            Detection((70.0, 10.0, 90.0, 30.0), 0.95, CanonicalRole.CAP, "cap"),
            Detection((105.0, 10.0, 120.0, 30.0), 0.95, CanonicalRole.CAP, "cap"),
        ]
    }
    clean_tracklets = BoTSortTracker().track_window(frames, SyntheticDetector(clean_boxes))
    assert len(clean_tracklets) == 2
    assert clean_tracklets[0].observations[0].cap_bbox_xyxy == (70.0, 10.0, 90.0, 30.0)
    assert clean_tracklets[0].observations[0].cap_ambiguous is False
    assert clean_tracklets[1].observations[0].cap_bbox_xyxy == (105.0, 10.0, 120.0, 30.0)
    assert clean_tracklets[1].observations[0].cap_ambiguous is False


# ---------------------------------------------------------------------------------------------
# Issue #157: downscaled detection, native rescaling, BALL/GOAL preservation
# ---------------------------------------------------------------------------------------------


def _downscaled_frame(index: int, time_sec: float, native: np.ndarray, det_width: int):
    """Build an ExtractedFrame whose detection image is a downscaled copy of ``native``."""
    height, width = native.shape[:2]
    det_height = round(height * det_width / width)
    ok, encoded = cv2.imencode(".jpg", native, [cv2.IMWRITE_JPEG_QUALITY, 95])
    assert ok
    return ExtractedFrame(
        index=index,
        video_time_sec=time_sec,
        image_bgr=cv2.resize(native, (det_width, det_height), interpolation=cv2.INTER_AREA),
        native_width=width,
        native_height=height,
        native_jpeg=encoded.tobytes(),
    )


def test_tracker_detects_on_downscaled_frames_and_returns_native_coordinates() -> None:
    native = np.zeros((720, 1280, 3), dtype=np.uint8)
    frames = [_downscaled_frame(i, 10.0 + i * 0.25, native, det_width=640) for i in range(3)]
    seen_shapes: list[tuple[int, ...]] = []

    class RecordingDetector(SyntheticDetector):
        def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
            seen_shapes.append(frame_bgr.shape)
            return super().detect(frame_bgr)

    # Boxes are expressed in the 640x360 detection image.
    det_box = (100.0, 50.0, 140.0, 140.0)
    detector = RecordingDetector(
        {i: [Detection(det_box, 0.9, CanonicalRole.PLAYER, "White team")] for i in range(3)}
    )

    tracklets = BoTSortTracker().track_window(frames, detector)

    assert seen_shapes == [(360, 640, 3)] * 3, "detector only sees the downscaled image"
    assert len(tracklets) == 1
    for obs in tracklets[0].observations:
        assert obs.bbox_xyxy == pytest.approx((200.0, 100.0, 280.0, 280.0))


def test_tracker_rescales_cap_boxes_and_keeps_native_cap_crops() -> None:
    native = np.zeros((720, 1280, 3), dtype=np.uint8)
    cv2.putText(native, "9", (230, 150), cv2.FONT_HERSHEY_SIMPLEX, 3.0, (255, 255, 255), 6)
    frame = _downscaled_frame(0, 10.0, native, det_width=640)
    player = Detection((100.0, 50.0, 160.0, 140.0), 0.9, CanonicalRole.PLAYER, "White team")
    cap = Detection((110.0, 55.0, 150.0, 85.0), 0.8, CanonicalRole.CAP, "cap")

    result = BoTSortTracker().track_window_with_scene(
        [frame], SyntheticDetector({0: [player, cap]})
    )
    obs = result.tracklets[0].observations[0]
    assert obs.cap_bbox_xyxy == pytest.approx((220.0, 110.0, 300.0, 170.0))

    selected = CropSelector().select_and_build_sheet(result.tracklets[0], [frame], max_crops=1)
    assert selected is not None
    crop_h, crop_w = selected.crops[0].crop_bgr.shape[:2]
    # Native cap box is 80x60 plus 20% padding per side, i.e. 112x84 native pixels.
    assert (crop_w, crop_h) == (112, 84)
    assert selected.crops[0].crop_bgr.max() > 200, (
        "crop comes from native pixels, not the thumbnail"
    )


def test_tracker_preserves_ball_and_goal_detections_without_changing_player_tracking() -> None:
    frames = [
        ExtractedFrame(
            index=i, video_time_sec=5.0 + i * 0.25, image_bgr=np.zeros((360, 640, 3), np.uint8)
        )
        for i in range(3)
    ]
    player = Detection((100.0, 100.0, 180.0, 220.0), 0.9, CanonicalRole.PLAYER, "White team")
    referee = Detection((300.0, 100.0, 340.0, 200.0), 0.9, CanonicalRole.REFEREE, "Referee")

    def scene(i: int) -> list[Detection]:
        return [
            player,
            referee,
            Detection(
                (200.0 + i * 5, 150.0, 215.0 + i * 5, 165.0), 0.7, CanonicalRole.BALL, "ball"
            ),
            Detection((500.0, 20.0, 620.0, 120.0), 0.85, CanonicalRole.GOAL, "Goal"),
        ]

    with_scene = BoTSortTracker().track_window_with_scene(
        frames, SyntheticDetector({i: scene(i) for i in range(3)})
    )
    players_only = BoTSortTracker().track_window(
        frames, SyntheticDetector({i: [player] for i in range(3)})
    )

    assert [d.frame_index for d in with_scene.ball_detections] == [0, 1, 2]
    assert with_scene.ball_detections[2].bbox_xyxy == (210.0, 150.0, 225.0, 165.0)
    assert with_scene.ball_detections[0].video_time_sec == 5.0
    assert [d.raw_class_name for d in with_scene.goal_detections] == ["Goal"] * 3
    assert with_scene.goal_detections[0].bbox_xyxy == (500.0, 20.0, 620.0, 120.0)

    # PLAYER/CAP tracking is unchanged: same tracklets whether or not extra roles are present,
    # and referees never become tracklets.
    assert [(t.track_id, len(t.observations)) for t in with_scene.tracklets] == [
        (t.track_id, len(t.observations)) for t in players_only
    ]
    assert len(with_scene.tracklets) == 1


def test_tracker_ball_and_goal_boxes_are_rescaled_to_native_coordinates() -> None:
    native = np.zeros((720, 1280, 3), dtype=np.uint8)
    frame = _downscaled_frame(0, 1.0, native, det_width=640)
    ball = Detection((10.0, 20.0, 20.0, 30.0), 0.6, CanonicalRole.BALL, "ball")
    result = BoTSortTracker().track_window_with_scene([frame], SyntheticDetector({0: [ball]}))
    assert result.ball_detections[0].bbox_xyxy == pytest.approx((20.0, 40.0, 40.0, 60.0))
    assert result.goal_detections == []
    assert BoTSortTracker().track_window_with_scene([], SyntheticDetector({})).tracklets == []


def test_camera_motion_translation_is_scaled_from_detection_to_native_pixels() -> None:
    frame = ExtractedFrame(
        index=0,
        video_time_sec=0.0,
        image_bgr=np.zeros((360, 640, 3), dtype=np.uint8),
        native_width=1280,
        native_height=720,
    )
    # Rotation/scale part is dimensionless; only the translation column scales.
    warp = np.array([[0.99, -0.01, 10.0], [0.01, 0.99, -4.0]])
    scaled = _scale_warp_translation(warp, frame)
    assert scaled is not None
    assert scaled[:, :2] == pytest.approx(warp[:, :2])
    assert scaled[:, 2] == pytest.approx([20.0, -8.0])
    assert warp[0, 2] == 10.0, "the input warp is not mutated"
    assert _scale_warp_translation(None, frame) is None

    native_frame = ExtractedFrame(index=1, video_time_sec=0.0, image_bgr=frame.image_bgr)
    assert _scale_warp_translation(warp, native_frame)[:, 2] == pytest.approx([10.0, -4.0])


def test_extracted_frame_defaults_to_native_without_compact_copy() -> None:
    image = np.zeros((90, 160, 3), dtype=np.uint8)
    frame = ExtractedFrame(index=3, video_time_sec=1.0, image_bgr=image)
    assert (frame.native_width, frame.native_height) == (160, 90)
    assert frame.scale_x == 1.0 and frame.scale_y == 1.0
    assert frame.native_bgr() is image

    corrupt = ExtractedFrame(
        index=4, video_time_sec=1.0, image_bgr=image, native_width=320, native_height=180,
        native_jpeg=b"not-a-jpeg",
    )  # fmt: skip
    with pytest.raises(RuntimeError, match="decode native copy"):
        corrupt.native_bgr()
