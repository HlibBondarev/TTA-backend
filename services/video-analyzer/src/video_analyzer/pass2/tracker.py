"""Unified BoT-SORT-style multi-object tracker with optical-flow camera motion compensation."""

from __future__ import annotations

from dataclasses import dataclass, field

import cv2
import numpy as np

from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.media.ffmpeg_client import ExtractedFrame


@dataclass(slots=True)
class TrackletObservation:
    """Single-frame observation belonging to a tracked object."""

    frame_index: int
    video_time_sec: float
    bbox_xyxy: tuple[float, float, float, float]
    confidence: float
    raw_class_name: str
    cap_bbox_xyxy: tuple[float, float, float, float] | None = None


@dataclass(slots=True)
class Tracklet:
    """Continuous object trajectory within a narrow Pass 2 4K window."""

    track_id: int
    observations: list[TrackletObservation] = field(default_factory=list)

    @property
    def mean_confidence(self) -> float:
        if not self.observations:
            return 0.0
        return sum(obs.confidence for obs in self.observations) / len(self.observations)

    def observation_nearest_to(self, video_time_sec: float) -> TrackletObservation | None:
        if not self.observations:
            return None
        return min(self.observations, key=lambda o: abs(o.video_time_sec - video_time_sec))


class _TrackState:
    """Internal mutable tracker state with velocity and camera-motion warp support."""

    def __init__(self, track_id: int, obs: TrackletObservation) -> None:
        self.track_id = track_id
        self.observations: list[TrackletObservation] = [obs]
        self.predicted_bbox: tuple[float, float, float, float] = obs.bbox_xyxy
        self.velocity: tuple[float, float] = (0.0, 0.0)
        self.missed_frames: int = 0

    def predict(self, affine_warp: np.ndarray | None) -> None:
        x1, y1, x2, y2 = self.observations[-1].bbox_xyxy
        vx, vy = self.velocity
        x1, y1, x2, y2 = x1 + vx * 0.5, y1 + vy * 0.5, x2 + vx * 0.5, y2 + vy * 0.5
        if affine_warp is not None:
            x1, y1, x2, y2 = _warp_bbox((x1, y1, x2, y2), affine_warp)
        self.predicted_bbox = (x1, y1, x2, y2)

    def update(self, obs: TrackletObservation) -> None:
        prev_cx, prev_cy = _bbox_center(self.observations[-1].bbox_xyxy)
        curr_cx, curr_cy = _bbox_center(obs.bbox_xyxy)
        self.velocity = (curr_cx - prev_cx, curr_cy - prev_cy)
        self.observations.append(obs)
        self.predicted_bbox = obs.bbox_xyxy
        self.missed_frames = 0


class SparseOpticalFlowGMC:
    """Global Motion Compensation for panning broadcast/poolside cameras."""

    def __init__(self, downscale_width: int = 640) -> None:
        self._downscale_width = downscale_width
        self._prev_gray: np.ndarray | None = None
        self._prev_scale: float = 1.0

    def compute_warp(self, frame_bgr: np.ndarray) -> np.ndarray | None:
        """Estimate 2x3 affine transformation from previous frame to current frame."""
        gray, scale = self._to_downscaled_gray(frame_bgr)
        if self._prev_gray is None:
            self._prev_gray = gray
            self._prev_scale = scale
            return None

        warp: np.ndarray | None = None
        prev_pts = cv2.goodFeaturesToTrack(
            self._prev_gray,
            maxCorners=200,
            qualityLevel=0.01,
            minDistance=20,
        )
        if prev_pts is not None and len(prev_pts) >= 6:
            curr_pts, status, _ = cv2.calcOpticalFlowPyrLK(self._prev_gray, gray, prev_pts, None)
            if curr_pts is not None and status is not None:
                valid = status.reshape(-1) == 1
                good_prev = prev_pts[valid]
                good_curr = curr_pts[valid]
                if len(good_prev) >= 6:
                    matrix, _ = cv2.estimateAffinePartial2D(
                        good_prev,
                        good_curr,
                        method=cv2.RANSAC,
                        ransacReprojThreshold=3.0,
                    )
                    if matrix is not None:
                        inv_scale = 1.0 / scale
                        scaled = matrix.copy()
                        scaled[0, 2] *= inv_scale
                        scaled[1, 2] *= inv_scale
                        warp = scaled

        self._prev_gray = gray
        self._prev_scale = scale
        return warp

    def _to_downscaled_gray(self, frame_bgr: np.ndarray) -> tuple[np.ndarray, float]:
        height, width = frame_bgr.shape[:2]
        if width <= self._downscale_width:
            return cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY), 1.0
        scale = self._downscale_width / float(width)
        new_size = (self._downscale_width, max(1, int(round(height * scale))))
        resized = cv2.resize(frame_bgr, new_size, interpolation=cv2.INTER_AREA)
        return cv2.cvtColor(resized, cv2.COLOR_BGR2GRAY), scale


class BoTSortTracker:
    """Two-stage (high/low conf) tracker with GMC over any BaseObjectDetector."""

    def __init__(
        self,
        high_conf_thresh: float = 0.45,
        iou_threshold: float = 0.25,
        max_lost_frames: int = 8,
    ) -> None:
        self._high_conf_thresh = high_conf_thresh
        self._iou_threshold = iou_threshold
        self._max_lost_frames = max_lost_frames

    def track_window(
        self,
        frames: list[ExtractedFrame],
        detector: BaseObjectDetector,
    ) -> list[Tracklet]:
        """Run object detection and multi-object tracking across all frames in a window."""
        if not frames:
            return []

        gmc = SparseOpticalFlowGMC()
        active_tracks: list[_TrackState] = []
        archived_tracks: list[Tracklet] = []
        next_track_id = 1

        for frame in frames:
            warp = gmc.compute_warp(frame.image_bgr)
            for track in active_tracks:
                track.predict(warp)

            detections = detector.detect(frame.image_bgr)
            player_dets = [d for d in detections if d.role == CanonicalRole.PLAYER]
            cap_dets = [d for d in detections if d.role == CanonicalRole.CAP]

            high_dets = [d for d in player_dets if d.confidence >= self._high_conf_thresh]
            low_dets = [d for d in player_dets if d.confidence < self._high_conf_thresh]

            matched_tracks, unmatched_track_indices, unmatched_high = self._associate(
                active_tracks, high_dets, self._iou_threshold
            )
            for trk_idx, det in matched_tracks:
                active_tracks[trk_idx].update(
                    self._build_observation(frame, det, cap_dets)
                )

            # Second association stage (ByteTrack / BoT-SORT low-confidence recovery)
            remaining_tracks = [active_tracks[i] for i in unmatched_track_indices]
            matched_low, still_unmatched_rel, _ = self._associate(
                remaining_tracks, low_dets, max(0.15, self._iou_threshold - 0.1)
            )
            for rel_idx, det in matched_low:
                remaining_tracks[rel_idx].update(
                    self._build_observation(frame, det, cap_dets)
                )

            for rel_idx in still_unmatched_rel:
                remaining_tracks[rel_idx].missed_frames += 1

            still_active: list[_TrackState] = []
            for track in active_tracks:
                if track.missed_frames > self._max_lost_frames:
                    archived_tracks.append(
                        Tracklet(track_id=track.track_id, observations=track.observations)
                    )
                else:
                    still_active.append(track)

            for det in unmatched_high:
                obs = self._build_observation(frame, det, cap_dets)
                still_active.append(_TrackState(track_id=next_track_id, obs=obs))
                next_track_id += 1

            active_tracks = still_active

        for track in active_tracks:
            archived_tracks.append(
                Tracklet(track_id=track.track_id, observations=track.observations)
            )

        archived_tracks.sort(key=lambda t: t.track_id)
        return archived_tracks

    def _associate(
        self,
        tracks: list[_TrackState],
        detections: list[Detection],
        iou_threshold: float,
    ) -> tuple[list[tuple[int, Detection]], list[int], list[Detection]]:
        if not tracks or not detections:
            return [], list(range(len(tracks))), list(detections)

        pairs: list[tuple[float, int, int]] = []
        for t_idx, track in enumerate(tracks):
            for d_idx, det in enumerate(detections):
                score = _bbox_iou(track.predicted_bbox, det.bbox_xyxy)
                if score >= iou_threshold:
                    pairs.append((score, t_idx, d_idx))

        pairs.sort(key=lambda item: item[0], reverse=True)
        used_tracks: set[int] = set()
        used_dets: set[int] = set()
        matches: list[tuple[int, Detection]] = []

        for _, t_idx, d_idx in pairs:
            if t_idx in used_tracks or d_idx in used_dets:
                continue
            used_tracks.add(t_idx)
            used_dets.add(d_idx)
            matches.append((t_idx, detections[d_idx]))

        unmatched_tracks = [i for i in range(len(tracks)) if i not in used_tracks]
        unmatched_dets = [detections[j] for j in range(len(detections)) if j not in used_dets]
        return matches, unmatched_tracks, unmatched_dets

    @staticmethod
    def _build_observation(
        frame: ExtractedFrame,
        player_det: Detection,
        cap_dets: list[Detection],
    ) -> TrackletObservation:
        cap_bbox = _match_cap_to_player(player_det.bbox_xyxy, cap_dets)
        return TrackletObservation(
            frame_index=frame.index,
            video_time_sec=frame.video_time_sec,
            bbox_xyxy=player_det.bbox_xyxy,
            confidence=player_det.confidence,
            raw_class_name=player_det.raw_class_name,
            cap_bbox_xyxy=cap_bbox,
        )


def _match_cap_to_player(
    player_box: tuple[float, float, float, float],
    cap_dets: list[Detection],
) -> tuple[float, float, float, float] | None:
    px1, py1, px2, py2 = player_box
    best_box: tuple[float, float, float, float] | None = None
    best_overlap = 0.0
    for cap in cap_dets:
        cx, cy = _bbox_center(cap.bbox_xyxy)
        if px1 <= cx <= px2 and py1 <= cy <= py2:
            overlap = _bbox_iou(player_box, cap.bbox_xyxy) + cap.confidence * 0.1
            if overlap > best_overlap:
                best_overlap = overlap
                best_box = cap.bbox_xyxy
    return best_box


def _bbox_center(box: tuple[float, float, float, float]) -> tuple[float, float]:
    return ((box[0] + box[2]) * 0.5, (box[1] + box[3]) * 0.5)


def _bbox_iou(
    box_a: tuple[float, float, float, float],
    box_b: tuple[float, float, float, float],
) -> float:
    ax1, ay1, ax2, ay2 = box_a
    bx1, by1, bx2, by2 = box_b
    ix1 = max(ax1, bx1)
    iy1 = max(ay1, by1)
    ix2 = min(ax2, bx2)
    iy2 = min(ay2, by2)
    iw = max(0.0, ix2 - ix1)
    ih = max(0.0, iy2 - iy1)
    inter = iw * ih
    if inter <= 0.0:
        return 0.0
    area_a = max(0.0, ax2 - ax1) * max(0.0, ay2 - ay1)
    area_b = max(0.0, bx2 - bx1) * max(0.0, by2 - by1)
    union = area_a + area_b - inter
    return inter / union if union > 0.0 else 0.0


def _warp_bbox(
    box: tuple[float, float, float, float],
    warp: np.ndarray,
) -> tuple[float, float, float, float]:
    pts = np.array(
        [[box[0], box[1], 1.0], [box[2], box[3], 1.0]],
        dtype=np.float64,
    )
    transformed = pts @ warp.T
    x1, y1 = float(transformed[0, 0]), float(transformed[0, 1])
    x2, y2 = float(transformed[1, 0]), float(transformed[1, 1])
    return (min(x1, x2), min(y1, y2), max(x1, x2), max(y1, y2))
