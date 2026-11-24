"""Native-resolution 4K cap crop extraction, sharpness ranking, and contact sheet builder."""

from __future__ import annotations

from dataclasses import dataclass
from math import ceil, sqrt
from pathlib import Path

import cv2
import numpy as np

from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass2.tracker import (
    Tracklet,
    TrackletObservation,
    compute_cap_crop_box,
)


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
    ) -> SelectedCrops | None:
        """Rank cap crops by sharpness and compose a numbered contact sheet for recognition."""
        frame_by_idx = {frame.index: frame for frame in frames}
        candidates: list[CropCandidate] = []

        for obs in tracklet.observations:
            frame = frame_by_idx.get(obs.frame_index)
            if frame is None:
                continue
            crop = self._extract_cap_region(frame.image_bgr, obs)
            if crop is None:
                continue
            score = self._score_crop_sharpness(crop)
            candidates.append(
                CropCandidate(
                    frame_index=obs.frame_index,
                    video_time_sec=obs.video_time_sec,
                    crop_bgr=crop,
                    sharpness_score=score,
                    cap_ambiguous=obs.cap_ambiguous,
                )
            )

        if not candidates:
            return None

        unambiguous = [c for c in candidates if not c.cap_ambiguous]
        pool = unambiguous if unambiguous else candidates
        has_ambiguous_cap = len(unambiguous) == 0

        selected = self._pick_diverse_top_k(pool, max_crops=max_crops)
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
            annotated = frame.image_bgr.copy()
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
    ) -> list[CropCandidate]:
        ranked = sorted(candidates, key=lambda c: c.sharpness_score, reverse=True)
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
    ) -> np.ndarray | None:
        img_h, img_w = frame_bgr.shape[:2]
        raw_x1, raw_y1, raw_x2, raw_y2 = compute_cap_crop_box(obs.bbox_xyxy, obs.cap_bbox_xyxy)
        x1 = max(0, int(round(raw_x1)))
        y1 = max(0, int(round(raw_y1)))
        x2 = min(img_w, int(round(raw_x2)))
        y2 = min(img_h, int(round(raw_y2)))

        if x2 - x1 < 8 or y2 - y1 < 8:
            return None
        return frame_bgr[y1:y2, x1:x2].copy()

    @staticmethod
    def _score_crop_sharpness(crop_bgr: np.ndarray) -> float:
        gray = cv2.cvtColor(crop_bgr, cv2.COLOR_BGR2GRAY)
        lap_var = float(cv2.Laplacian(gray, cv2.CV_64F).var())
        area = crop_bgr.shape[0] * crop_bgr.shape[1]
        return lap_var * sqrt(max(1, area))
