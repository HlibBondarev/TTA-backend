"""Unified BoT-SORT-style multi-object tracker with optical-flow camera motion compensation."""

from __future__ import annotations

import math
from collections import Counter
from dataclasses import dataclass, field

import cv2
import numpy as np

from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.sport.profile import DEFAULT_CAP_ZONE_GEOMETRY, CapZoneGeometry

_MIN_GMC_POINTS = 6
DEFAULT_TRACK_BIRTH_CONF = 0.30
"""Minimum detector confidence for an unmatched player detection to start a new tracklet."""
DEFAULT_MAX_BALLS_PER_FRAME = 1
"""A match has one ball in play: keep only the most confident ball detection per frame."""
DEFAULT_HIGH_CONF_THRESH = 0.45
"""Confidence boundary between first-stage (high) and recovery-stage (low) association."""

BBox = tuple[float, float, float, float]


@dataclass(slots=True)
class TrackletObservation:
    """Single-frame observation belonging to a tracked object."""

    frame_index: int
    video_time_sec: float
    bbox_xyxy: tuple[float, float, float, float]
    confidence: float
    raw_class_name: str
    cap_bbox_xyxy: tuple[float, float, float, float] | None = None
    cap_ambiguous: bool = False


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

    def label_votes(
        self,
        center_sec: float | None = None,
        half_window_sec: float | None = None,
    ) -> Counter[str]:
        """Count normalized detector labels, optionally limited to a time window.

        Args:
            center_sec: Window center. When ``None`` every observation is counted.
            half_window_sec: Window half-width in seconds. Required with ``center_sec``.

        Returns:
            Counter of stripped, lower-cased ``raw_class_name`` values.
        """
        votes: Counter[str] = Counter()
        for obs in self.observations:
            if (
                center_sec is not None
                and half_window_sec is not None
                and abs(obs.video_time_sec - center_sec) > half_window_sec
            ):
                continue
            votes[obs.raw_class_name.strip().lower()] += 1
        return votes


@dataclass(frozen=True, slots=True)
class FrameDetection:
    """A non-player scene detection (ball or goal) in native pixel coordinates."""

    frame_index: int
    video_time_sec: float
    bbox_xyxy: tuple[float, float, float, float]
    confidence: float
    raw_class_name: str
    role: str = ""
    suppressed: bool = False
    """True for a raw detection dropped by a per-frame filter (kept for debug overlays only)."""


@dataclass(slots=True)
class TrackingResult:
    """Tracker output: player/cap tracklets plus per-frame ball and goal detections.

    Ball and goal detections are carried through unchanged so later stages (ball-proximity actor
    scoring) can use them without another tracker refactor.
    """

    tracklets: list[Tracklet] = field(default_factory=list)
    ball_detections: list[FrameDetection] = field(default_factory=list)
    goal_detections: list[FrameDetection] = field(default_factory=list)
    all_detections: list[FrameDetection] = field(default_factory=list)
    """Every raw detection per frame for debug overlays; filtered ones are ``suppressed``."""


class _TrackState:
    """Internal mutable tracker state with velocity and camera-motion warp support."""

    def __init__(self, track_id: int, obs: TrackletObservation) -> None:
        self.track_id = track_id
        self.observations: list[TrackletObservation] = [obs]
        self.predicted_bbox: tuple[float, float, float, float] = obs.bbox_xyxy
        self._warped_last_obs_bbox: tuple[float, float, float, float] = obs.bbox_xyxy
        self.velocity: tuple[float, float] = (0.0, 0.0)
        self.missed_frames: int = 0

    def predict(self, affine_warp: np.ndarray | None) -> None:
        box = self.predicted_bbox
        if affine_warp is not None:
            box = _warp_bbox(box, affine_warp)
            self._warped_last_obs_bbox = _warp_bbox(self._warped_last_obs_bbox, affine_warp)
        vx, vy = self.velocity
        dx, dy = vx * 0.5, vy * 0.5
        self.predicted_bbox = (box[0] + dx, box[1] + dy, box[2] + dx, box[3] + dy)

    def update(self, obs: TrackletObservation) -> None:
        ref_cx, ref_cy = _bbox_center(self._warped_last_obs_bbox)
        curr_cx, curr_cy = _bbox_center(obs.bbox_xyxy)
        steps = max(1, self.missed_frames + 1)
        self.velocity = ((curr_cx - ref_cx) / steps, (curr_cy - ref_cy) / steps)
        self.observations.append(obs)
        self.predicted_bbox = obs.bbox_xyxy
        self._warped_last_obs_bbox = obs.bbox_xyxy
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
        prev_gray = self._prev_gray
        self._prev_gray = gray
        self._prev_scale = scale
        if prev_gray is None:
            return None

        matrix = self._estimate_partial_affine(prev_gray, gray)
        if matrix is None:
            return None
        # The affine was estimated on downscaled frames; translate it back to input pixels.
        inv_scale = 1.0 / scale
        warp = matrix.copy()
        warp[0, 2] *= inv_scale
        warp[1, 2] *= inv_scale
        return warp

    @staticmethod
    def _estimate_partial_affine(prev_gray: np.ndarray, gray: np.ndarray) -> np.ndarray | None:
        """Track sparse corners between two gray frames and fit a RANSAC partial affine."""
        prev_pts = cv2.goodFeaturesToTrack(
            prev_gray,
            maxCorners=200,
            qualityLevel=0.01,
            minDistance=20,
        )
        if prev_pts is None or len(prev_pts) < _MIN_GMC_POINTS:
            return None
        curr_pts, status, _ = cv2.calcOpticalFlowPyrLK(prev_gray, gray, prev_pts, None)
        if curr_pts is None or status is None:
            return None
        valid = status.reshape(-1) == 1
        good_prev = prev_pts[valid]
        good_curr = curr_pts[valid]
        if len(good_prev) < _MIN_GMC_POINTS:
            return None
        matrix, _ = cv2.estimateAffinePartial2D(
            good_prev,
            good_curr,
            method=cv2.RANSAC,
            ransacReprojThreshold=3.0,
        )
        return matrix

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
        high_conf_thresh: float = DEFAULT_HIGH_CONF_THRESH,
        iou_threshold: float = 0.25,
        max_lost_frames: int = 8,
        track_birth_conf: float = DEFAULT_TRACK_BIRTH_CONF,
    ) -> None:
        self._high_conf_thresh = high_conf_thresh
        self._track_birth_conf = min(track_birth_conf, high_conf_thresh)
        self._iou_threshold = iou_threshold
        self._max_lost_frames = max_lost_frames

    def track_window(
        self,
        frames: list[ExtractedFrame],
        detector: BaseObjectDetector,
    ) -> list[Tracklet]:
        """Run detection and tracking across a window and return only the player tracklets."""
        return self.track_window_with_scene(frames, detector).tracklets

    def track_window_with_scene(
        self,
        frames: list[ExtractedFrame],
        detector: BaseObjectDetector,
        geometry: CapZoneGeometry = DEFAULT_CAP_ZONE_GEOMETRY,
        max_balls_per_frame: int = DEFAULT_MAX_BALLS_PER_FRAME,
    ) -> TrackingResult:
        """Track players across a window and also preserve per-frame ball/goal detections.

        Detection runs on each frame's (possibly downscaled) ``image_bgr``; every returned box,
        including ball and goal boxes, is rescaled to native pixel coordinates.

        Args:
            frames: Extracted window frames in temporal order.
            detector: Object detector applied to every frame.
            geometry: Sport-specific head/cap zone used for cap crops and ambiguity checks.
            max_balls_per_frame: Ball detections kept per frame, highest confidence first.
        """
        result = TrackingResult()
        if not frames:
            return result

        gmc = SparseOpticalFlowGMC()
        active_tracks: list[_TrackState] = []
        archived_tracks: list[Tracklet] = []
        next_track_id = 1

        for frame in frames:
            warp = _scale_warp_translation(gmc.compute_warp(frame.image_bgr), frame)
            for track in active_tracks:
                track.predict(warp)

            detections = _detect_in_native_coordinates(detector, frame)
            _collect_scene_detections(result, frame, detections, max_balls_per_frame)
            player_dets = [d for d in detections if d.role == CanonicalRole.PLAYER]
            cap_dets = [d for d in detections if d.role == CanonicalRole.CAP]
            cap_assignments = _match_caps_to_players(player_dets, cap_dets, geometry)

            high_dets = [d for d in player_dets if d.confidence >= self._high_conf_thresh]
            low_dets = [d for d in player_dets if d.confidence < self._high_conf_thresh]

            matched_tracks, unmatched_track_indices, unmatched_high = self._associate(
                active_tracks, high_dets, self._iou_threshold
            )
            for trk_idx, det in matched_tracks:
                active_tracks[trk_idx].update(self._build_observation(frame, det, cap_assignments))

            # Second association stage (ByteTrack / BoT-SORT low-confidence recovery)
            remaining_tracks = [active_tracks[i] for i in unmatched_track_indices]
            matched_low, still_unmatched_rel, unmatched_low = self._associate(
                remaining_tracks, low_dets, max(0.15, self._iou_threshold - 0.1)
            )
            for rel_idx, det in matched_low:
                remaining_tracks[rel_idx].update(
                    self._build_observation(frame, det, cap_assignments)
                )

            for rel_idx in still_unmatched_rel:
                remaining_tracks[rel_idx].missed_frames += 1

            still_active = self._archive_expired_tracks(active_tracks, archived_tracks)

            births = unmatched_high + [
                det for det in unmatched_low if det.confidence >= self._track_birth_conf
            ]
            for det in births:
                obs = self._build_observation(frame, det, cap_assignments)
                still_active.append(_TrackState(track_id=next_track_id, obs=obs))
                next_track_id += 1

            active_tracks = still_active

        for track in active_tracks:
            archived_tracks.append(
                Tracklet(track_id=track.track_id, observations=track.observations)
            )

        archived_tracks.sort(key=lambda t: t.track_id)
        result.tracklets = archived_tracks
        return result

    def _archive_expired_tracks(
        self,
        active_tracks: list[_TrackState],
        archived_tracks: list[Tracklet],
    ) -> list[_TrackState]:
        """Move tracks lost for too long into ``archived_tracks``; return the still-active ones."""
        still_active: list[_TrackState] = []
        for track in active_tracks:
            if track.missed_frames > self._max_lost_frames:
                archived_tracks.append(
                    Tracklet(track_id=track.track_id, observations=track.observations)
                )
            else:
                still_active.append(track)
        return still_active

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
        cap_assignments: dict[int, tuple[tuple[float, float, float, float] | None, bool]],
    ) -> TrackletObservation:
        cap_bbox, cap_ambiguous = cap_assignments.get(id(player_det), (None, False))
        return TrackletObservation(
            frame_index=frame.index,
            video_time_sec=frame.video_time_sec,
            bbox_xyxy=player_det.bbox_xyxy,
            confidence=player_det.confidence,
            raw_class_name=player_det.raw_class_name,
            cap_bbox_xyxy=cap_bbox,
            cap_ambiguous=cap_ambiguous,
        )


def _scale_warp_translation(warp: np.ndarray | None, frame: ExtractedFrame) -> np.ndarray | None:
    """Convert a camera-motion warp from detection-image pixels to native pixels.

    With ``S = diag(scale_x, scale_y)`` the native warp is ``S @ A @ S^-1`` for the linear part
    and ``S @ t`` for the translation. For unequal axis scales the cross-axis terms therefore
    scale by ``scale_x / scale_y`` (``[0, 1]``) and ``scale_y / scale_x`` (``[1, 0]``).
    """
    if warp is None:
        return None
    scaled = warp.copy()
    scaled[0, 1] *= frame.scale_x / frame.scale_y
    scaled[1, 0] *= frame.scale_y / frame.scale_x
    scaled[0, 2] *= frame.scale_x
    scaled[1, 2] *= frame.scale_y
    return scaled


def _detect_in_native_coordinates(
    detector: BaseObjectDetector,
    frame: ExtractedFrame,
) -> list[Detection]:
    """Run the detector on the detection image and rescale its boxes to native coordinates."""
    detections = detector.detect(frame.image_bgr)
    scale_x, scale_y = frame.scale_x, frame.scale_y
    if math.isclose(scale_x, 1.0) and math.isclose(scale_y, 1.0):
        return detections
    return [
        Detection(
            bbox_xyxy=(
                det.bbox_xyxy[0] * scale_x,
                det.bbox_xyxy[1] * scale_y,
                det.bbox_xyxy[2] * scale_x,
                det.bbox_xyxy[3] * scale_y,
            ),
            confidence=det.confidence,
            role=det.role,
            raw_class_name=det.raw_class_name,
        )
        for det in detections
    ]


def _collect_scene_detections(
    result: TrackingResult,
    frame: ExtractedFrame,
    detections: list[Detection],
    max_balls_per_frame: int = DEFAULT_MAX_BALLS_PER_FRAME,
) -> None:
    """Record scene detections, keeping at most ``max_balls_per_frame`` balls per frame.

    Every raw detection lands in ``all_detections`` (balls dropped by the limit are marked
    ``suppressed``); only kept balls reach ``ball_detections`` and thus actor scoring.
    """
    kept_balls = _top_ball_ids(detections, max_balls_per_frame)
    for det in detections:
        is_dropped_ball = det.role == CanonicalRole.BALL and id(det) not in kept_balls
        record = FrameDetection(
            frame_index=frame.index,
            video_time_sec=frame.video_time_sec,
            bbox_xyxy=det.bbox_xyxy,
            confidence=det.confidence,
            raw_class_name=det.raw_class_name,
            role=str(det.role),
            suppressed=is_dropped_ball,
        )
        result.all_detections.append(record)
        if is_dropped_ball:
            continue
        if det.role == CanonicalRole.BALL:
            result.ball_detections.append(record)
        elif det.role == CanonicalRole.GOAL:
            result.goal_detections.append(record)


def _top_ball_ids(detections: list[Detection], limit: int) -> set[int]:
    """Return ``id()`` of the ``limit`` most confident ball detections (stable on ties)."""
    balls = [det for det in detections if det.role == CanonicalRole.BALL]
    balls.sort(key=lambda det: det.confidence, reverse=True)
    return {id(det) for det in balls[: max(0, limit)]}


def compute_cap_crop_box(
    player_bbox_xyxy: BBox,
    cap_bbox_xyxy: BBox | None = None,
    geometry: CapZoneGeometry = DEFAULT_CAP_ZONE_GEOMETRY,
) -> BBox:
    """Return the padded crop coordinates used for cap number recognition."""
    bx1, by1, bx2, by2 = (
        cap_bbox_xyxy
        if cap_bbox_xyxy is not None
        else upper_body_cap_zone(player_bbox_xyxy, geometry)
    )
    pad_x = (bx2 - bx1) * geometry.padding
    pad_y = (by2 - by1) * geometry.padding
    return (bx1 - pad_x, by1 - pad_y, bx2 + pad_x, by2 + pad_y)


def upper_body_cap_zone(
    player_bbox_xyxy: BBox,
    geometry: CapZoneGeometry = DEFAULT_CAP_ZONE_GEOMETRY,
) -> BBox:
    """Estimate the unpadded upper-body cap zone for a player bounding box."""
    px1, py1, px2, py2 = player_bbox_xyxy
    pw = px2 - px1
    ph = py2 - py1
    inset = pw * geometry.horizontal_inset
    return (px1 + inset, py1, px2 - inset, py1 + ph * geometry.top_fraction)


def _boxes_overlap(
    box_a: tuple[float, float, float, float],
    box_b: tuple[float, float, float, float],
) -> bool:
    """Return True when two xyxy bounding boxes have positive intersection area."""
    return min(box_a[2], box_b[2]) > max(box_a[0], box_b[0]) and min(box_a[3], box_b[3]) > max(
        box_a[1], box_b[1]
    )


def _index_contained_caps(
    player_dets: list[Detection],
    cap_dets: list[Detection],
) -> tuple[dict[int, list[int]], dict[int, list[int]]]:
    caps_per_player: dict[int, list[int]] = {idx: [] for idx in range(len(player_dets))}
    players_per_cap: dict[int, list[int]] = {idx: [] for idx in range(len(cap_dets))}

    for p_idx, p_det in enumerate(player_dets):
        px1, py1, px2, py2 = p_det.bbox_xyxy
        for c_idx, c_det in enumerate(cap_dets):
            cx, cy = _bbox_center(c_det.bbox_xyxy)
            if px1 <= cx <= px2 and py1 <= cy <= py2:
                caps_per_player[p_idx].append(c_idx)
                players_per_cap[c_idx].append(p_idx)

    return caps_per_player, players_per_cap


@dataclass(frozen=True, slots=True)
class _CapScene:
    """Per-frame player/cap detections plus the geometry used to flag shared crops."""

    player_dets: list[Detection]
    cap_dets: list[Detection]
    unique_cap_idx_by_player: dict[int, int]
    geometry: CapZoneGeometry


def _crop_overlaps_neighbor_cap_or_head(
    crop_box: BBox,
    player_idx: int,
    owned_cap_idx: int | None,
    scene: _CapScene,
) -> bool:
    """Flag a crop box that overlaps another cap or an unowned neighbor head zone."""
    for other_c_idx, c_det in enumerate(scene.cap_dets):
        if other_c_idx != owned_cap_idx and _boxes_overlap(crop_box, c_det.bbox_xyxy):
            return True
    for other_idx, other_det in enumerate(scene.player_dets):
        if other_idx == player_idx or other_idx in scene.unique_cap_idx_by_player:
            continue
        if _boxes_overlap(crop_box, upper_body_cap_zone(other_det.bbox_xyxy, scene.geometry)):
            return True
    return False


def _resolve_player_cap_assignment(
    p_idx: int,
    contained_caps: list[int],
    players_per_cap: dict[int, list[int]],
    scene: _CapScene,
) -> tuple[BBox | None, bool]:
    p_det = scene.player_dets[p_idx]
    if not contained_caps:
        fallback_box = compute_cap_crop_box(p_det.bbox_xyxy, None, scene.geometry)
        return (None, _crop_overlaps_neighbor_cap_or_head(fallback_box, p_idx, None, scene))
    if len(contained_caps) > 1:
        return (None, True)

    c_idx = contained_caps[0]
    if len(players_per_cap[c_idx]) > 1:
        return (None, True)

    cap_box = scene.cap_dets[c_idx].bbox_xyxy
    crop_box = compute_cap_crop_box(p_det.bbox_xyxy, cap_box, scene.geometry)
    return (cap_box, _crop_overlaps_neighbor_cap_or_head(crop_box, p_idx, c_idx, scene))


def _match_caps_to_players(
    player_dets: list[Detection],
    cap_dets: list[Detection],
    geometry: CapZoneGeometry = DEFAULT_CAP_ZONE_GEOMETRY,
) -> dict[int, tuple[BBox | None, bool]]:
    """Assign each cap detection across all players, flagging contested or overlapping crops."""
    if not player_dets:
        return {}

    caps_per_player, players_per_cap = _index_contained_caps(player_dets, cap_dets)
    unique_cap_idx_by_player: dict[int, int] = {
        p_idx: contained[0]
        for p_idx, contained in caps_per_player.items()
        if len(contained) == 1 and len(players_per_cap[contained[0]]) == 1
    }

    scene = _CapScene(player_dets, cap_dets, unique_cap_idx_by_player, geometry)
    return {
        id(p_det): _resolve_player_cap_assignment(
            p_idx, caps_per_player[p_idx], players_per_cap, scene
        )
        for p_idx, p_det in enumerate(player_dets)
    }


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
    x1, y1, x2, y2 = box
    pts = np.array(
        [
            [x1, y1, 1.0],
            [x2, y1, 1.0],
            [x2, y2, 1.0],
            [x1, y2, 1.0],
        ],
        dtype=np.float64,
    )
    transformed = pts @ warp.T
    xs = transformed[:, 0]
    ys = transformed[:, 1]
    return (float(xs.min()), float(ys.min()), float(xs.max()), float(ys.max()))
