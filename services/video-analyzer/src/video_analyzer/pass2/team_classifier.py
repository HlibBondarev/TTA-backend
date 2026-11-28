"""Sport-agnostic per-tracklet team classification from detector labels and cap color.

The classifier never hardcodes team colors: tracked/opponent colors come from runtime hints
and the goalkeeper token/color from the active :class:`SportProfile`. Two independent signals
vote per observation near the event time:

1. the detector label (``"White team"``, ``"Goalkeeper"``, ...), and
2. a lightweight HSV pixel vote on the head/cap zone of the same observation.
"""

from __future__ import annotations

from collections import Counter
from collections.abc import Mapping
from dataclasses import dataclass, field
from enum import StrEnum
from typing import TYPE_CHECKING

import cv2
import numpy as np

from video_analyzer.pass2.color_constants import (
    COLOR_ZONE_BOTTOM_FRACTION,
    COLOR_ZONE_HORIZONTAL_INSET,
    COLOR_ZONE_TOP_FRACTION,
    GOALKEEPER_PANEL_MIN_SHARE,
    HSV_VOTE_WEIGHT,
    LABEL_VOTE_WEIGHT,
    MIN_COLOR_PIXEL_SHARE,
    MIN_HSV_VOTES_TO_OVERRIDE_OPPONENT_LABEL,
    MIN_ZONE_SIDE_PX,
    NAMED_COLOR_HSV_RANGES,
    OPPONENT_EXCLUSION_SHARE,
    TEAM_WINDOW_HALF_SEC,
    TRACKED_CONFIRM_SHARE,
    canonical_color,
    normalize_color_name,
)
from video_analyzer.pass2.tracker import BBox, Tracklet, TrackletObservation, upper_body_cap_zone
from video_analyzer.sport.profile import DEFAULT_CAP_ZONE_GEOMETRY, CapZoneGeometry

if TYPE_CHECKING:
    from video_analyzer.media.ffmpeg_client import ExtractedFrame
    from video_analyzer.sport.context import AnalysisContext


class TeamClass(StrEnum):
    """Team membership of a tracklet relative to the tracked team."""

    TRACKED = "tracked"
    OPPONENT = "opponent"
    GOALKEEPER = "goalkeeper"
    UNKNOWN = "unknown"


@dataclass(frozen=True, slots=True)
class TeamEvidence:
    """Structured result of classifying one tracklet around the event time."""

    team_class: TeamClass = TeamClass.UNKNOWN
    confidence: float = 0.0
    label_votes: dict[str, int] = field(default_factory=dict)
    hsv_votes: dict[str, int] = field(default_factory=dict)
    goalkeeper_evidence: bool = False
    observation_count: int = 0
    tracked_share: float = 0.0
    """Weighted share of votes (labels and cap color) pointing at the tracked team."""

    def to_debug_dict(self) -> dict[str, object]:
        """Return a JSON-serializable breakdown for debug artifacts."""
        return {
            "teamClass": str(self.team_class),
            "confidence": round(self.confidence, 4),
            "labelVotes": dict(self.label_votes),
            "hsvVotes": dict(self.hsv_votes),
            "goalkeeperEvidence": self.goalkeeper_evidence,
            "observationCount": self.observation_count,
            "trackedShare": round(self.tracked_share, 4),
        }


@dataclass(frozen=True, slots=True)
class TeamClassificationSpec:
    """Colors, tokens and geometry the classifier votes against (no sport knowledge inside)."""

    tracked_color: str | None = None
    opponent_color: str | None = None
    goalkeeper_token: str | None = None
    goalkeeper_color: str | None = None
    geometry: CapZoneGeometry = DEFAULT_CAP_ZONE_GEOMETRY

    @classmethod
    def from_context(cls, context: AnalysisContext) -> TeamClassificationSpec:
        """Build the spec from runtime hints and the sport profile of ``context``."""
        profile = context.profile
        return cls(
            tracked_color=context.hints.tracked_cap_color_hint,
            opponent_color=context.hints.opponent_cap_color_hint,
            goalkeeper_token=profile.goalkeeper_label_token,
            goalkeeper_color=profile.goalkeeper_cap_color,
            geometry=profile.cap_zone_geometry,
        )

    def goalkeeper_panel_color(self) -> str | None:
        """Canonical goalkeeper cap color usable as a *panel* signal on a team-color base.

        ``None`` when the sport has no goalkeeper cap color, the name is unknown, or the color
        equals a team color (then it cannot distinguish goalkeepers from field players).
        """
        goalkeeper = canonical_color(self.goalkeeper_color)
        team_colors = {canonical_color(self.tracked_color), canonical_color(self.opponent_color)}
        return goalkeeper if goalkeeper is not None and goalkeeper not in team_colors else None

    def hsv_palette(self) -> dict[str, TeamClass]:
        """Map each *known* canonical color to the team class it votes for.

        Unknown color names are skipped silently. When two roles share a color, the earlier
        role (tracked, opponent, goalkeeper) wins.
        """
        palette: dict[str, TeamClass] = {}
        roles = (
            (self.tracked_color, TeamClass.TRACKED),
            (self.opponent_color, TeamClass.OPPONENT),
            (self.goalkeeper_color, TeamClass.GOALKEEPER),
        )
        for name, team_class in roles:
            canonical = canonical_color(name)
            if canonical is not None:
                palette.setdefault(canonical, team_class)
        return palette


def head_zone_color_shares(
    frame_bgr: np.ndarray,
    box: BBox,
    color_names: list[str],
) -> dict[str, float]:
    """Return the pixel share of each canonical color inside ``box`` of a BGR frame.

    Args:
        frame_bgr: Decoded frame in BGR order.
        box: ``(x1, y1, x2, y2)`` zone in the frame's pixel coordinates.
        color_names: Canonical color names present in ``NAMED_COLOR_HSV_RANGES``.

    Returns:
        Mapping of color name to a 0..1 share; empty when the zone is too small or off-frame.
    """
    height, width = frame_bgr.shape[:2]
    x1, y1 = max(0, int(round(box[0]))), max(0, int(round(box[1])))
    x2, y2 = min(width, int(round(box[2]))), min(height, int(round(box[3])))
    if x2 - x1 < MIN_ZONE_SIDE_PX or y2 - y1 < MIN_ZONE_SIDE_PX:
        return {}
    hsv = cv2.cvtColor(frame_bgr[y1:y2, x1:x2], cv2.COLOR_BGR2HSV)
    total = float(hsv.shape[0] * hsv.shape[1])
    return {name: _color_pixel_count(hsv, name) / total for name in color_names}


def _color_pixel_count(hsv: np.ndarray, color_name: str) -> int:
    mask = np.zeros(hsv.shape[:2], dtype=np.uint8)
    for rng in NAMED_COLOR_HSV_RANGES[color_name]:
        lower = np.array([rng.hue_min, rng.sat_min, rng.val_min], dtype=np.uint8)
        upper = np.array([rng.hue_max, rng.sat_max, rng.val_max], dtype=np.uint8)
        mask |= cv2.inRange(hsv, lower, upper)
    return int(np.count_nonzero(mask))


def dominant_head_color(
    frame_bgr: np.ndarray,
    box: BBox,
    color_names: list[str],
    min_share: float = MIN_COLOR_PIXEL_SHARE,
) -> str | None:
    """Return the named color covering the largest share of ``box`` (at least ``min_share``)."""
    shares = head_zone_color_shares(frame_bgr, box, color_names)
    if not shares:
        return None
    best_name = max(shares, key=lambda name: shares[name])
    return best_name if shares[best_name] >= min_share else None


def observation_head_zone(obs: TrackletObservation, geometry: CapZoneGeometry) -> BBox:
    """Return the unpadded head/cap zone of an observation (detected cap box when available)."""
    if obs.cap_bbox_xyxy is not None:
        return obs.cap_bbox_xyxy
    return upper_body_cap_zone(obs.bbox_xyxy, geometry)


def observation_color_zone(obs: TrackletObservation, geometry: CapZoneGeometry) -> BBox:
    """Return the zone sampled for cap color: the top-center of the detector box.

    A detected cap box is used as-is. Otherwise only the middle of the box width and its upper
    half are sampled, so face, skin and the water at the box edges do not dilute the vote.
    ``geometry`` only shapes the crop zone and is not used here.
    """
    del geometry
    if obs.cap_bbox_xyxy is not None:
        return obs.cap_bbox_xyxy
    x1, y1, x2, y2 = obs.bbox_xyxy
    width, height = x2 - x1, y2 - y1
    return (
        x1 + width * COLOR_ZONE_HORIZONTAL_INSET,
        y1 + height * COLOR_ZONE_TOP_FRACTION,
        x2 - width * COLOR_ZONE_HORIZONTAL_INSET,
        y1 + height * COLOR_ZONE_BOTTOM_FRACTION,
    )


@dataclass(slots=True)
class _VoteTally:
    """Weighted vote counts of one tracklet window (labels are noisy, HSV is trusted more)."""

    label_votes: Counter[TeamClass] = field(default_factory=Counter)
    hsv_votes: Counter[TeamClass] = field(default_factory=Counter)
    goalkeeper_label_flags: int = 0
    goalkeeper_hsv_flags: int = 0
    observations: int = 0
    hsv_observations: int = 0

    @property
    def tracked_promotion_blocked(self) -> bool:
        """True when the only tracked evidence is too thin to overrule an opponent label."""
        return (
            self.label_votes[TeamClass.OPPONENT] > 0
            and self.label_votes[TeamClass.TRACKED] == 0
            and self.hsv_votes[TeamClass.TRACKED] < MIN_HSV_VOTES_TO_OVERRIDE_OPPONENT_LABEL
        )

    def class_weight(self, team_class: TeamClass) -> float:
        return (
            LABEL_VOTE_WEIGHT * self.label_votes[team_class]
            + HSV_VOTE_WEIGHT * self.hsv_votes[team_class]
        )

    @property
    def total_weight(self) -> float:
        return sum(self.class_weight(team_class) for team_class in TeamClass)

    @property
    def goalkeeper_ratio(self) -> float:
        """Strongest independent goalkeeper signal: label token or cap-color panels.

        Each signal is measured against the observations it could see, so a goalkeeper label is
        not discounted just because the cap-color vote shows only the team-color base.
        """
        label_ratio = self.goalkeeper_label_flags / max(1, self.observations)
        hsv_ratio = self.goalkeeper_hsv_flags / max(1, self.hsv_observations)
        return max(label_ratio, hsv_ratio)


@dataclass(frozen=True, slots=True)
class _HsvVote:
    """HSV result of one observation: a team-class vote and a goalkeeper-panel flag."""

    team_class: TeamClass | None = None
    goalkeeper_flag: bool = False


def _label_mentions(label: str, color: str | None) -> bool:
    """Return True when the lower-cased detector label names ``color`` (or its canonical form)."""
    name = normalize_color_name(color)
    if not name:
        return False
    canonical = canonical_color(name)
    return name in label or (canonical is not None and canonical in label)


def _label_vote(label: str, spec: TeamClassificationSpec) -> tuple[TeamClass | None, bool]:
    """Return ``(team vote, goalkeeper flag)`` for a detector label.

    A team color in the label takes priority over the goalkeeper token, but the token still
    raises the goalkeeper flag so goalkeeper events can be required to show such evidence.
    """
    token = (spec.goalkeeper_token or "").strip().lower()
    is_goalkeeper = bool(token) and token in label
    if _label_mentions(label, spec.opponent_color):
        return TeamClass.OPPONENT, is_goalkeeper
    if _label_mentions(label, spec.tracked_color):
        return TeamClass.TRACKED, is_goalkeeper
    return (TeamClass.GOALKEEPER if is_goalkeeper else None), is_goalkeeper


class TeamClassifier:
    """Weighted vote classifier over tracklet observations near the event time.

    Detector labels carry a lower weight than head-zone HSV votes (``LABEL_VOTE_WEIGHT`` vs
    ``HSV_VOTE_WEIGHT``) because the detector frequently mislabels cap colors. Without frames
    only labels vote, which reduces to plain label shares.
    """

    def __init__(
        self,
        window_half_sec: float = TEAM_WINDOW_HALF_SEC,
        min_pixel_share: float = MIN_COLOR_PIXEL_SHARE,
        opponent_share: float = OPPONENT_EXCLUSION_SHARE,
        tracked_share: float = TRACKED_CONFIRM_SHARE,
    ) -> None:
        self._window_half_sec = window_half_sec
        self._min_pixel_share = min_pixel_share
        self._opponent_share = opponent_share
        self._tracked_share = tracked_share

    def classify(
        self,
        tracklet: Tracklet,
        event_time_sec: float,
        frames_by_index: Mapping[int, ExtractedFrame] | None,
        spec: TeamClassificationSpec,
    ) -> TeamEvidence:
        """Classify ``tracklet`` from observations within the window around ``event_time_sec``.

        Args:
            tracklet: Candidate player tracklet.
            event_time_sec: Event time used to select observations.
            frames_by_index: Frames keyed by index for HSV voting; ``None`` disables HSV.
            spec: Colors, goalkeeper token and head-zone geometry to vote against.

        Returns:
            Structured :class:`TeamEvidence`; ``UNKNOWN`` when no signal is available.
        """
        observations = self._window_observations(tracklet, event_time_sec)
        palette = spec.hsv_palette()
        panel_color = spec.goalkeeper_panel_color()
        native_cache: dict[int, np.ndarray | None] = {}
        tally = _VoteTally(observations=len(observations))
        for obs in observations:
            self._add_label_vote(tally, obs, spec)
            native = self._native_frame(obs, frames_by_index, native_cache)
            if native is None or not palette:
                continue
            tally.hsv_observations += 1
            hsv = self._hsv_vote(native, obs, palette, panel_color, spec)
            if hsv.team_class is not None:
                tally.hsv_votes[hsv.team_class] += 1
            tally.goalkeeper_hsv_flags += int(hsv.goalkeeper_flag)
        return self._resolve(tally)

    def _window_observations(
        self, tracklet: Tracklet, event_time_sec: float
    ) -> list[TrackletObservation]:
        in_window = [
            obs
            for obs in tracklet.observations
            if abs(obs.video_time_sec - event_time_sec) <= self._window_half_sec
        ]
        if in_window:
            return in_window
        nearest = tracklet.observation_nearest_to(event_time_sec)
        return [nearest] if nearest is not None else []

    @staticmethod
    def _add_label_vote(
        tally: _VoteTally, obs: TrackletObservation, spec: TeamClassificationSpec
    ) -> None:
        team_vote, is_goalkeeper = _label_vote(obs.raw_class_name.strip().lower(), spec)
        if team_vote is not None:
            tally.label_votes[team_vote] += 1
        tally.goalkeeper_label_flags += int(is_goalkeeper)

    @staticmethod
    def _native_frame(
        obs: TrackletObservation,
        frames_by_index: Mapping[int, ExtractedFrame] | None,
        native_cache: dict[int, np.ndarray | None],
    ) -> np.ndarray | None:
        if frames_by_index is None:
            return None
        frame = frames_by_index.get(obs.frame_index)
        if frame is None:
            return None
        if obs.frame_index not in native_cache:
            native_cache[obs.frame_index] = frame.native_bgr()
        return native_cache[obs.frame_index]

    def _hsv_vote(
        self,
        native: np.ndarray,
        obs: TrackletObservation,
        palette: dict[str, TeamClass],
        panel_color: str | None,
        spec: TeamClassificationSpec,
    ) -> _HsvVote:
        """Vote for the dominant named cap color and flag small goalkeeper-color panels.

        Bi-colored goalkeeper caps show a team-color base with small goalkeeper-color panels:
        the base wins the team vote while the panels still raise the goalkeeper flag. Panels
        without any dominant base color vote for the goalkeeper class itself.
        """
        zone = observation_color_zone(obs, spec.geometry)
        names = list(palette) + (
            [panel_color] if panel_color and panel_color not in palette else []
        )
        shares = head_zone_color_shares(native, zone, names)
        if not shares:
            return _HsvVote()
        winner = max(palette, key=lambda name: shares[name])
        team_class = palette[winner] if shares[winner] >= self._min_pixel_share else None
        panel_flag = (
            bool(panel_color) and shares.get(panel_color, 0.0) >= GOALKEEPER_PANEL_MIN_SHARE
        )
        if team_class is None and panel_flag:
            team_class = TeamClass.GOALKEEPER
        return _HsvVote(team_class, panel_flag or team_class is TeamClass.GOALKEEPER)

    def _resolve(self, tally: _VoteTally) -> TeamEvidence:
        label_breakdown = {str(k): v for k, v in tally.label_votes.items()}
        hsv_breakdown = {str(k): v for k, v in tally.hsv_votes.items()}
        total = tally.total_weight
        if total <= 0.0:
            return TeamEvidence(
                label_votes=label_breakdown,
                hsv_votes=hsv_breakdown,
                observation_count=tally.observations,
            )
        shares = {cls: tally.class_weight(cls) / total for cls in TeamClass}
        goalkeeper_share = tally.goalkeeper_ratio
        goalkeeper_evidence = goalkeeper_share >= self._tracked_share
        team_class, confidence = self._pick_class(
            shares, goalkeeper_evidence, goalkeeper_share, tally.tracked_promotion_blocked
        )
        return TeamEvidence(
            team_class=team_class,
            confidence=round(confidence, 4),
            label_votes=label_breakdown,
            hsv_votes=hsv_breakdown,
            goalkeeper_evidence=goalkeeper_evidence,
            observation_count=tally.observations,
            tracked_share=shares[TeamClass.TRACKED],
        )

    def _pick_class(
        self,
        shares: dict[TeamClass, float],
        goalkeeper_evidence: bool,
        goalkeeper_share: float,
        tracked_promotion_blocked: bool = False,
    ) -> tuple[TeamClass, float]:
        """Resolve the class: opponent first, then our goalkeeper, then the tracked team.

        A goalkeeper cap in the opponent color (opponent base plus goalkeeper panels) is the
        opponent goalkeeper and stays ``OPPONENT``. Goalkeeper evidence next to tracked-team
        evidence (tracked base plus goalkeeper panels) is our goalkeeper.

        ``tracked_promotion_blocked`` keeps a tracklet the detector explicitly called
        ``opponent`` at ``UNKNOWN`` unless several HSV observations say tracked.
        """
        if shares[TeamClass.OPPONENT] >= self._opponent_share:
            return TeamClass.OPPONENT, shares[TeamClass.OPPONENT]
        if goalkeeper_evidence:
            return TeamClass.GOALKEEPER, max(goalkeeper_share, shares[TeamClass.GOALKEEPER])
        if shares[TeamClass.TRACKED] >= self._tracked_share and not tracked_promotion_blocked:
            return TeamClass.TRACKED, shares[TeamClass.TRACKED]
        return TeamClass.UNKNOWN, max(shares.values())
