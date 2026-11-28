"""Native-resolution 4K cap crop extraction, sharpness ranking, and contact sheet builder.

Crops are cut from each frame's native pixels (decoded on demand from the compact in-memory
copy), even though detection ran on a downscaled image.
"""

from __future__ import annotations

from dataclasses import dataclass
from math import ceil, sqrt
from pathlib import Path

import cv2
import numpy as np

from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass2.color_constants import (
    MIN_NEAR_EVENT_OBSERVATIONS,
    NEAR_EVENT_WINDOW_SEC,
)
from video_analyzer.pass2.crop_filters import (
    REJECT_ASPECT_OUTLIER,
    REJECT_COLOR_MISMATCH,
    REJECT_CROP_TOO_SMALL,
    REJECT_OUTSIDE_NEAR_WINDOW,
    CropSelectionContext,
    aspect_ratio_outlier_frames,
    color_mismatch,
    neighbor_has_opponent_color,
)
from video_analyzer.pass2.tracker import (
    Tracklet,
    TrackletObservation,
    compute_cap_crop_box,
)
from video_analyzer.sport.profile import DEFAULT_CAP_ZONE_GEOMETRY, CapZoneGeometry

ObservationReport = list[dict[str, object]]
"""Per-observation selection verdicts collected for debug artifacts."""

_CROP_MIN_SIDE_PX = 8
_STATUS_REJECTED = "rejected"
_STATUS_SELECTED = "selected"


@dataclass(slots=True)
class CropCandidate:
    """Single cap crop with sharpness and quality score."""

    frame_index: int
    video_time_sec: float
    crop_bgr: np.ndarray
    sharpness_score: float
    cap_ambiguous: bool = False


@dataclass(slots=True)
class SelectedCrops:
    """Top-K sharp cap crops plus composed JPEG contact sheet bytes."""

    crops: list[CropCandidate]
    contact_sheet_jpeg: bytes
    contact_sheet_bgr: np.ndarray
    has_ambiguous_cap: bool = False


class CropSelector:
    """Selects crisp, temporally diverse cap crops from native-resolution frames."""

    def __init__(self, tile_size: int = 256, min_frame_gap: int = 2) -> None:
        self._tile_size = tile_size
        self._min_frame_gap = min_frame_gap

    def select_and_build_sheet(
        self,
        tracklet: Tracklet,
        frames: list[ExtractedFrame],
        max_crops: int = 6,
        selection_context: CropSelectionContext | None = None,
        observation_report: ObservationReport | None = None,
    ) -> SelectedCrops | None:
        """Rank cap crops by sharpness and compose a numbered contact sheet for recognition.

        Args:
            tracklet: Tracklet of the matched actor.
            frames: Window frames carrying the native pixels.
            max_crops: Maximum number of crops on the contact sheet.
            selection_context: Optional event evidence. When given, crops are limited to
                the near-event window (with fallback), mismatching team colors and merged
                boxes are dropped, and neighbors wearing the opponent color mark a crop as
                ambiguous.
            observation_report: Optional list that receives one verdict per observation.
        """
        report: ObservationReport = observation_report if observation_report is not None else []
        frame_by_idx = {frame.index: frame for frame in frames}
        outliers = (
            aspect_ratio_outlier_frames(tracklet.observations) if selection_context else set()
        )
        candidates: list[CropCandidate] = []
        for obs in tracklet.observations:
            candidate = self._build_candidate(
                obs, frame_by_idx.get(obs.frame_index), outliers, selection_context, report
            )
            if candidate is not None:
                candidates.append(candidate)
        if not candidates:
            return None

        candidates = self._prefer_near_event(candidates, selection_context, report)
        unambiguous = [c for c in candidates if not c.cap_ambiguous]
        pool = unambiguous if unambiguous else candidates
        has_ambiguous_cap = len(unambiguous) == 0
        event_time = selection_context.event_time_sec if selection_context else None

        selected = self._pick_diverse_top_k(pool, max_crops=max_crops, event_time_sec=event_time)
        _mark_selected(report, selected)
        sheet_bgr = self._build_contact_sheet(selected)
        ok, encoded = cv2.imencode(".jpg", sheet_bgr, [cv2.IMWRITE_JPEG_QUALITY, 95])
        if not ok:
            return None

        return SelectedCrops(
            crops=selected,
            contact_sheet_jpeg=encoded.tobytes(),
            contact_sheet_bgr=sheet_bgr,
            has_ambiguous_cap=has_ambiguous_cap,
        )

    def _build_candidate(
        self,
        obs: TrackletObservation,
        frame: ExtractedFrame | None,
        outliers: set[int],
        ctx: CropSelectionContext | None,
        report: ObservationReport,
    ) -> CropCandidate | None:
        """Cut and score one observation's crop, recording why it was rejected if it is."""
        if frame is None:
            return None
        if obs.frame_index in outliers:
            report.append(_verdict(obs, REJECT_ASPECT_OUTLIER))
            return None
        native = frame.native_bgr()
        geometry = ctx.geometry if ctx else DEFAULT_CAP_ZONE_GEOMETRY
        crop = self._extract_cap_region(native, obs, geometry)
        if crop is None:
            report.append(_verdict(obs, REJECT_CROP_TOO_SMALL))
            return None
        if ctx is not None and color_mismatch(native, obs, ctx):
            report.append(_verdict(obs, REJECT_COLOR_MISMATCH))
            return None
        ambiguous = obs.cap_ambiguous
        if ctx is not None and not ambiguous:
            crop_box = compute_cap_crop_box(obs.bbox_xyxy, obs.cap_bbox_xyxy, geometry)
            ambiguous = neighbor_has_opponent_color(native, obs, crop_box, ctx)
        return CropCandidate(
            frame_index=obs.frame_index,
            video_time_sec=obs.video_time_sec,
            crop_bgr=crop,
            sharpness_score=self._score_crop_sharpness(crop),
            cap_ambiguous=ambiguous,
        )

    @staticmethod
    def _prefer_near_event(
        candidates: list[CropCandidate],
        ctx: CropSelectionContext | None,
        report: ObservationReport,
    ) -> list[CropCandidate]:
        """Keep near-event candidates when at least two exist, else fall back to all."""
        if ctx is None:
            return candidates
        near = [
            c
            for c in candidates
            if abs(c.video_time_sec - ctx.event_time_sec) <= NEAR_EVENT_WINDOW_SEC
        ]
        if len(near) < MIN_NEAR_EVENT_OBSERVATIONS:
            return candidates
        near_frames = {c.frame_index for c in near}
        for candidate in candidates:
            if candidate.frame_index not in near_frames:
                report.append(
                    _report_entry(
                        candidate.frame_index,
                        candidate.video_time_sec,
                        _STATUS_REJECTED,
                        reason=REJECT_OUTSIDE_NEAR_WINDOW,
                    )
                )
        return near

    @staticmethod
    def save_debug_artifacts(
        debug_dir: Path,
        event_idx: int,
        selected: SelectedCrops,
        frames: list[ExtractedFrame],
        obs: TrackletObservation,
    ) -> None:
        """Persist annotated keyframe and cap contact sheet for visual verification."""
        debug_dir.mkdir(parents=True, exist_ok=True)
        sheet_path = debug_dir / f"event_{event_idx:03d}_crops.jpg"
        cv2.imwrite(str(sheet_path), selected.contact_sheet_bgr)

        frame = next((f for f in frames if f.index == obs.frame_index), None)
        if frame is not None:
            annotated = frame.native_bgr().copy()
            x1, y1, x2, y2 = (int(round(v)) for v in obs.bbox_xyxy)
            cv2.rectangle(annotated, (x1, y1), (x2, y2), (0, 255, 0), 4)
            label = f"{obs.raw_class_name} ({obs.confidence:.2f})"
            cv2.putText(
                annotated,
                label,
                (x1, max(30, y1 - 12)),
                cv2.FONT_HERSHEY_SIMPLEX,
                1.0,
                (0, 255, 0),
                2,
            )
            keyframe_path = debug_dir / f"event_{event_idx:03d}_keyframe.jpg"
            cv2.imwrite(str(keyframe_path), annotated)

    def _pick_diverse_top_k(
        self,
        candidates: list[CropCandidate],
        max_crops: int,
        event_time_sec: float | None = None,
    ) -> list[CropCandidate]:
        ranked = sorted(candidates, key=lambda c: _rank_score(c, event_time_sec), reverse=True)
        chosen: list[CropCandidate] = []

        for candidate in ranked:
            if len(chosen) >= max_crops:
                break
            too_close = any(
                abs(candidate.frame_index - prev.frame_index) < self._min_frame_gap
                for prev in chosen
            )
            if too_close:
                continue
            chosen.append(candidate)

        if len(chosen) < max_crops:
            chosen_indices = {c.frame_index for c in chosen}
            for candidate in ranked:
                if len(chosen) >= max_crops:
                    break
                if candidate.frame_index not in chosen_indices:
                    chosen.append(candidate)
                    chosen_indices.add(candidate.frame_index)

        chosen.sort(key=lambda c: c.frame_index)
        return chosen

    def _build_contact_sheet(self, crops: list[CropCandidate]) -> np.ndarray:
        cols = min(3, max(1, len(crops)))
        rows = ceil(len(crops) / cols)
        tile = self._tile_size
        canvas = np.full((rows * tile, cols * tile, 3), 24, dtype=np.uint8)

        for idx, crop in enumerate(crops):
            r = idx // cols
            c = idx % cols
            tile_img = self._resize_with_padding(crop.crop_bgr, tile)
            cv2.rectangle(tile_img, (0, 0), (62, 30), (0, 0, 0), -1)
            cv2.putText(
                tile_img,
                f"#{idx + 1}",
                (8, 22),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.7,
                (255, 255, 255),
                2,
            )
            y0 = r * tile
            x0 = c * tile
            canvas[y0 : y0 + tile, x0 : x0 + tile] = tile_img

        return canvas

    @staticmethod
    def _resize_with_padding(image: np.ndarray, tile_size: int) -> np.ndarray:
        h, w = image.shape[:2]
        scale = min(tile_size / max(1, w), tile_size / max(1, h))
        new_w = max(1, int(round(w * scale)))
        new_h = max(1, int(round(h * scale)))
        interp = cv2.INTER_CUBIC if scale > 1.0 else cv2.INTER_AREA
        resized = cv2.resize(image, (new_w, new_h), interpolation=interp)
        tile = np.full((tile_size, tile_size, 3), 24, dtype=np.uint8)
        y_off = (tile_size - new_h) // 2
        x_off = (tile_size - new_w) // 2
        tile[y_off : y_off + new_h, x_off : x_off + new_w] = resized
        return tile

    @staticmethod
    def _extract_cap_region(
        frame_bgr: np.ndarray,
        obs: TrackletObservation,
        geometry: CapZoneGeometry = DEFAULT_CAP_ZONE_GEOMETRY,
    ) -> np.ndarray | None:
        img_h, img_w = frame_bgr.shape[:2]
        raw_x1, raw_y1, raw_x2, raw_y2 = compute_cap_crop_box(
            obs.bbox_xyxy, obs.cap_bbox_xyxy, geometry
        )
        x1 = max(0, int(round(raw_x1)))
        y1 = max(0, int(round(raw_y1)))
        x2 = min(img_w, int(round(raw_x2)))
        y2 = min(img_h, int(round(raw_y2)))

        if x2 - x1 < _CROP_MIN_SIDE_PX or y2 - y1 < _CROP_MIN_SIDE_PX:
            return None
        return frame_bgr[y1:y2, x1:x2].copy()

    @staticmethod
    def _score_crop_sharpness(crop_bgr: np.ndarray) -> float:
        gray = cv2.cvtColor(crop_bgr, cv2.COLOR_BGR2GRAY)
        lap_var = float(cv2.Laplacian(gray, cv2.CV_64F).var())
        area = crop_bgr.shape[0] * crop_bgr.shape[1]
        return lap_var * sqrt(max(1, area))


def _rank_score(candidate: CropCandidate, event_time_sec: float | None) -> float:
    """Sharpness weighted by temporal proximity to the event (unweighted without a time)."""
    if event_time_sec is None:
        return candidate.sharpness_score
    return candidate.sharpness_score / (1.0 + abs(candidate.video_time_sec - event_time_sec))


def _report_entry(
    frame_index: int, video_time_sec: float, status: str, **details: object
) -> dict[str, object]:
    return {
        "frameIndex": frame_index,
        "videoTimeSec": video_time_sec,
        "status": status,
        **details,
    }


def _verdict(obs: TrackletObservation, reason: str) -> dict[str, object]:
    return _report_entry(obs.frame_index, obs.video_time_sec, _STATUS_REJECTED, reason=reason)


def _mark_selected(report: ObservationReport, selected: list[CropCandidate]) -> None:
    for candidate in selected:
        report.append(
            _report_entry(
                candidate.frame_index,
                candidate.video_time_sec,
                _STATUS_SELECTED,
                sharpness=round(candidate.sharpness_score, 2),
                ambiguous=candidate.cap_ambiguous,
            )
        )
