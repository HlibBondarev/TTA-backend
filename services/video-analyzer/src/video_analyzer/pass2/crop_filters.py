"""Observation filters that keep cap crops on a single, team-consistent actor."""

from __future__ import annotations

from dataclasses import dataclass
from statistics import median

import numpy as np

from video_analyzer.pass2.color_constants import (
    ASPECT_RATIO_TOLERANCE,
    MIN_OBSERVATIONS_FOR_ASPECT_FILTER,
    canonical_color,
)
from video_analyzer.pass2.team_classifier import dominant_head_color, observation_color_zone
from video_analyzer.pass2.tracker import (
    BBox,
    Tracklet,
    TrackletObservation,
    _boxes_overlap,
)
from video_analyzer.sport.profile import DEFAULT_CAP_ZONE_GEOMETRY, CapZoneGeometry

REJECT_ASPECT_OUTLIER = "ASPECT_RATIO_OUTLIER"
REJECT_COLOR_MISMATCH = "HSV_COLOR_MISMATCH"
REJECT_CROP_TOO_SMALL = "CROP_TOO_SMALL"
REJECT_OUTSIDE_NEAR_WINDOW = "OUTSIDE_NEAR_EVENT_WINDOW"


@dataclass(frozen=True, slots=True)
class CropSelectionContext:
    """Event-level evidence used to filter and rank cap crops of one tracklet.

    Attributes:
        event_time_sec: Event time used for the near-event window and temporal ranking.
        geometry: Sport head/cap zone geometry.
        expected_color: Cap color the actor is expected to wear (tracked or goalkeeper color).
        opponent_color: Opponent cap color used to flag contested neighbor head zones.
        extra_colors: Further named colors (tracked, goalkeeper) included when sampling HSV.
        other_tracklets: All other tracklets of the window, for neighbor ambiguity checks.
    """

    event_time_sec: float
    geometry: CapZoneGeometry = DEFAULT_CAP_ZONE_GEOMETRY
    expected_color: str | None = None
    opponent_color: str | None = None
    extra_colors: tuple[str, ...] = ()
    other_tracklets: tuple[Tracklet, ...] = ()

    def palette(self) -> list[str]:
        """Canonical, known colors sampled in a head zone (unknown names are skipped)."""
        names = (self.expected_color, self.opponent_color, *self.extra_colors)
        canonical = (canonical_color(name) for name in names)
        return list(dict.fromkeys(name for name in canonical if name is not None))

    @property
    def expected_canonical(self) -> str | None:
        return canonical_color(self.expected_color)

    @property
    def opponent_canonical(self) -> str | None:
        return canonical_color(self.opponent_color)


def _aspect_ratio(box: BBox) -> float:
    return (box[2] - box[0]) / max(1e-6, box[3] - box[1])


def aspect_ratio_outlier_frames(observations: list[TrackletObservation]) -> set[int]:
    """Return frame indices whose box aspect ratio deviates from the tracklet median.

    A box far wider than usual typically covers two merged players. Needs at least three
    observations, and never flags every observation.
    """
    if len(observations) < MIN_OBSERVATIONS_FOR_ASPECT_FILTER:
        return set()
    ratios = {obs.frame_index: _aspect_ratio(obs.bbox_xyxy) for obs in observations}
    reference = median(ratios.values())
    if reference <= 0.0:
        return set()
    outliers = {
        frame_index
        for frame_index, ratio in ratios.items()
        if abs(ratio - reference) / reference > ASPECT_RATIO_TOLERANCE
    }
    return outliers if len(outliers) < len(ratios) else set()


def color_mismatch(
    native_bgr: np.ndarray, obs: TrackletObservation, ctx: CropSelectionContext
) -> bool:
    """Return True when the head zone clearly shows a named color other than the expected one."""
    expected = ctx.expected_canonical
    palette = ctx.palette()
    if expected is None or not palette:
        return False
    observed = dominant_head_color(native_bgr, observation_color_zone(obs, ctx.geometry), palette)
    return observed is not None and observed != expected


def neighbor_has_opponent_color(
    native_bgr: np.ndarray,
    obs: TrackletObservation,
    crop_box: BBox,
    ctx: CropSelectionContext,
) -> bool:
    """Return True when an overlapping neighbor's head zone shows the opponent cap color."""
    opponent = ctx.opponent_canonical
    if opponent is None:
        return False
    for other in ctx.other_tracklets:
        neighbor = next((o for o in other.observations if o.frame_index == obs.frame_index), None)
        if neighbor is None:
            continue
        zone = observation_color_zone(neighbor, ctx.geometry)
        if not _boxes_overlap(crop_box, zone):
            continue
        if dominant_head_color(native_bgr, zone, [opponent]) == opponent:
            return True
    return False
