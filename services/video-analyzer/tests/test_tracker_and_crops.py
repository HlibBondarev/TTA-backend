"""Tests for BoT-SORT panning tracklet association, crop sharpness selection, and OCR."""

from __future__ import annotations

from types import SimpleNamespace

import cv2
import numpy as np

from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.actor_matcher import ActorMatcher
from video_analyzer.pass2.cap_resolver import GeminiCropGridCapResolver, LocalOcrCapResolver
from video_analyzer.pass2.crop_selector import CropSelector
from video_analyzer.pass2.tracker import BoTSortTracker
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
