"""Per-event Pass 2 debug artifacts and per-job attribution counters.

Artifacts are written whenever a debug directory is configured, including for events whose
Pass 2 failed or matched no tracklet, so unmatched events can still be diagnosed visually.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import cv2
import numpy as np

from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass2.actor_matcher import ActorMatchResult
from video_analyzer.pass2.cap_resolver import CapResolutionResult
from video_analyzer.pass2.tracker import BBox, FrameDetection, TrackingResult, Tracklet

_COLOR_DETECTION = (0, 200, 255)
_COLOR_TARGET = (255, 128, 0)
_COLOR_CHOSEN = (0, 255, 0)
_COLOR_SUPPRESSED = (160, 160, 160)
_FONT = cv2.FONT_HERSHEY_SIMPLEX
_DETECTION_THICKNESS = 2
_HIGHLIGHT_THICKNESS = 4
_LABEL_SCALE = 0.6
_LABEL_MIN_Y = 24
_LABEL_OFFSET_Y = 8
SUMMARY_FILENAME = "pass2_summary.json"


def detections_image_path(debug_dir: Path, event_idx: int) -> Path:
    """Return the path of the all-detections overlay for an event."""
    return debug_dir / f"event_{event_idx:03d}_all_detections.jpg"


def event_json_path(debug_dir: Path, event_idx: int) -> Path:
    """Return the path of the Pass 2 JSON breakdown for an event."""
    return debug_dir / f"event_{event_idx:03d}_pass2.json"


def chosen_bbox_on_frame(tracklet: Tracklet | None, frame_index: int) -> BBox | None:
    """Return the chosen tracklet's box on ``frame_index``, or ``None`` if it has none there.

    The highlight must belong to the displayed frame: the observation nearest to the event in
    time may sit on a different frame, and drawing its box would point at the wrong place.
    """
    if tracklet is None:
        return None
    obs = next((o for o in tracklet.observations if o.frame_index == frame_index), None)
    return obs.bbox_xyxy if obs is not None else None


def nearest_frame(frames: list[ExtractedFrame], event_time_sec: float) -> ExtractedFrame | None:
    """Return the frame closest in time to the event, or ``None`` for an empty window."""
    if not frames:
        return None
    return min(frames, key=lambda frame: abs(frame.video_time_sec - event_time_sec))


def _draw_box(image: np.ndarray, box: BBox, color: tuple[int, int, int], thickness: int) -> None:
    x1, y1, x2, y2 = (int(round(value)) for value in box)
    cv2.rectangle(image, (x1, y1), (x2, y2), color, thickness)


def _draw_label(image: np.ndarray, box: BBox, text: str, color: tuple[int, int, int]) -> None:
    x1, y1 = int(round(box[0])), int(round(box[1]))
    cv2.putText(
        image,
        text,
        (x1, max(_LABEL_MIN_Y, y1 - _LABEL_OFFSET_Y)),
        _FONT,
        _LABEL_SCALE,
        color,
        _DETECTION_THICKNESS,
    )


def _detections_for_frame(tracking: TrackingResult, frame_index: int) -> list[FrameDetection]:
    return [det for det in tracking.all_detections if det.frame_index == frame_index]


def write_detection_overlay(
    debug_dir: Path,
    event_idx: int,
    frame: ExtractedFrame,
    tracking: TrackingResult,
    target_bbox_normalized: list[float] | None,
    chosen_bbox: BBox | None,
) -> Path:
    """Draw every detection of the event frame plus the Pass 1 target and chosen tracklet.

    Args:
        debug_dir: Output directory (created when missing).
        event_idx: One-based event index used in the file name.
        frame: Frame closest to the event time.
        tracking: Tracking output carrying all per-frame detections.
        target_bbox_normalized: Pass 1 actor box as ``[x1, y1, x2, y2]`` fractions, if any.
        chosen_bbox: Native-pixel box of the chosen tracklet in this frame, if any.

    Returns:
        Path of the written JPEG.
    """
    debug_dir.mkdir(parents=True, exist_ok=True)
    image = frame.native_bgr().copy()
    for det in _detections_for_frame(tracking, frame.index):
        color = _COLOR_SUPPRESSED if det.suppressed else _COLOR_DETECTION
        suffix = " (suppressed)" if det.suppressed else ""
        _draw_box(image, det.bbox_xyxy, color, _DETECTION_THICKNESS)
        _draw_label(
            image,
            det.bbox_xyxy,
            f"{det.role}:{det.raw_class_name} {det.confidence:.2f}{suffix}",
            color,
        )
    if target_bbox_normalized is not None:
        height, width = image.shape[:2]
        nx1, ny1, nx2, ny2 = target_bbox_normalized
        target = (nx1 * width, ny1 * height, nx2 * width, ny2 * height)
        _draw_box(image, target, _COLOR_TARGET, _HIGHLIGHT_THICKNESS)
        _draw_label(image, target, "pass1 target", _COLOR_TARGET)
    if chosen_bbox is not None:
        _draw_box(image, chosen_bbox, _COLOR_CHOSEN, _HIGHLIGHT_THICKNESS)
        _draw_label(image, chosen_bbox, "chosen", _COLOR_CHOSEN)
    path = detections_image_path(debug_dir, event_idx)
    cv2.imwrite(str(path), image)
    return path


@dataclass(frozen=True, slots=True)
class Pass2DebugRecord:
    """Everything the per-event JSON reports about one Pass 2 attribution."""

    event_index: int
    video_time_sec: float
    event_name: str
    failure_reason: str | None
    match_result: ActorMatchResult | None
    matched_summary: dict[str, Any] | None
    crop_report: list[dict[str, object]]
    cap_result: CapResolutionResult | None
    expected_cap_color: str | None
    opponent_cap_color: str | None
    gate_outcomes: dict[str, bool]
    review_reasons: list[str]


def build_event_payload(record: Pass2DebugRecord) -> dict[str, Any]:
    """Build the JSON-serializable per-event Pass 2 breakdown."""
    match = record.match_result
    cap = record.cap_result
    return {
        "eventIndex": record.event_index,
        "videoTimeSec": record.video_time_sec,
        "eventName": record.event_name,
        "failureReason": record.failure_reason,
        "matchedTracklet": record.matched_summary,
        "candidates": [b.to_debug_dict() for b in match.breakdowns] if match else [],
        "opponentExclusions": match.opponent_exclusions if match else 0,
        "ballDetectionsInWindow": match.ball_detections_in_window if match else 0,
        "cropObservations": record.crop_report,
        "ocr": None
        if cap is None
        else {
            "resolvedCapNumber": cap.resolved_cap_number,
            "confidence": cap.confidence,
            "observedCapColor": cap.observed_cap_color,
            "tileCapColors": list(cap.tile_cap_colors),
            "expectedCapColor": record.expected_cap_color,
            "opponentCapColor": record.opponent_cap_color,
            "reasoning": cap.reasoning,
            "rawResponse": cap.raw_response,
        },
        "gates": record.gate_outcomes,
        "reviewReasons": record.review_reasons,
    }


def write_event_json(debug_dir: Path, event_idx: int, payload: dict[str, Any]) -> Path:
    """Write the per-event Pass 2 JSON breakdown."""
    debug_dir.mkdir(parents=True, exist_ok=True)
    path = event_json_path(debug_dir, event_idx)
    path.write_text(json.dumps(payload, indent=2, default=str), encoding="utf-8")
    return path


@dataclass(slots=True)
class Pass2Counters:
    """Per-job actor-attribution counters (reported via debug JSON, logs and warnings)."""

    events: int = 0
    opponent_exclusions: int = 0
    ocr_color_rejections: int = 0
    ball_detections_in_windows: int = 0
    events_with_ball_evidence: int = 0
    per_event_ball_counts: list[int] = field(default_factory=list)

    def record_event(
        self,
        match_result: ActorMatchResult | None,
        ocr_color_rejected: bool,
        min_ball_detections: int,
    ) -> None:
        """Accumulate one event's attribution outcome."""
        self.events += 1
        self.ocr_color_rejections += int(ocr_color_rejected)
        if match_result is None:
            return
        self.opponent_exclusions += match_result.opponent_exclusions
        ball_count = match_result.ball_detections_in_window
        self.ball_detections_in_windows += ball_count
        self.per_event_ball_counts.append(ball_count)
        self.events_with_ball_evidence += int(ball_count >= min_ball_detections)

    @property
    def mean_ball_detections(self) -> float:
        """Mean ball detections per event window (0.0 when no event was tracked)."""
        if not self.per_event_ball_counts:
            return 0.0
        return self.ball_detections_in_windows / len(self.per_event_ball_counts)

    def needs_warning(self) -> bool:
        """Return True when opponent exclusions or OCR color rejections occurred."""
        return bool(self.opponent_exclusions or self.ocr_color_rejections)

    def summary_line(self) -> str:
        """Return a concise one-line summary of the counters."""
        return (
            f"Pass 2 actor attribution: {self.opponent_exclusions} opponent tracklet "
            f"exclusion(s), {self.ocr_color_rejections} OCR cap-color rejection(s), "
            f"{self.mean_ball_detections:.1f} ball detections per event window "
            f"({self.events_with_ball_evidence}/{self.events} events with ball evidence)."
        )

    def to_dict(self) -> dict[str, object]:
        """Return a JSON-serializable view of the counters."""
        return {
            "events": self.events,
            "opponentTrackletExclusions": self.opponent_exclusions,
            "ocrCapColorRejections": self.ocr_color_rejections,
            "meanBallDetectionsPerWindow": round(self.mean_ball_detections, 3),
            "eventsWithBallEvidence": self.events_with_ball_evidence,
            "summary": self.summary_line(),
        }


def write_summary_json(debug_dir: Path, counters: Pass2Counters) -> Path:
    """Write the per-job Pass 2 counters next to the per-event artifacts."""
    debug_dir.mkdir(parents=True, exist_ok=True)
    path = debug_dir / SUMMARY_FILENAME
    path.write_text(json.dumps(counters.to_dict(), indent=2), encoding="utf-8")
    return path
