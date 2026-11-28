"""Named cap-color HSV table and shared Pass 2 color/threshold constants.

OpenCV HSV convention: hue 0-179, saturation 0-255, value 0-255.

Calibration notes (Clip 1, dim poolside broadcast footage, 3840x2160):

* Pool water is *saturated* teal-blue (hue ~98-108, saturation ~100-140, value ~85-100), so a
  plain ``blue`` range around hue 100 matches most water pixels and must not be used.
* Caps are nearly *achromatic* in that footage: white caps are bright and unsaturated
  (saturation <~60), navy caps are dark and unsaturated (saturation ~25-90, value ~50-80).
* Saturation therefore separates caps from water, and hue separates royal blue caps
  (hue >= 112) from water (hue <= 108).
"""

from __future__ import annotations

import re
from dataclasses import dataclass

# --- Team classification thresholds -------------------------------------------------------
TEAM_WINDOW_HALF_SEC: float = 1.0
"""Half-width of the window around the event time used for team-evidence voting."""

OPPONENT_EXCLUSION_SHARE: float = 0.60
"""Opponent vote share at or above which a tracklet is hard-excluded."""

TRACKED_CONFIRM_SHARE: float = 0.50
"""Tracked-team vote share at or above which a tracklet is classified as the tracked team."""

MIN_COLOR_PIXEL_SHARE: float = 0.20
"""Minimum share of head-zone pixels a color must cover to cast an HSV vote."""

MIN_HSV_VOTES_TO_OVERRIDE_OPPONENT_LABEL: int = 2
"""Tracked HSV observations needed to overrule an explicit ``opponent`` detector label.

Excluding an opponent is conservative, so one contradicting HSV frame may do it. Promoting a
tracklet the detector called ``opponent`` to ``TRACKED`` is not: a single splash or glare frame
must not be enough, so that case needs at least this many tracked HSV observations.
"""

MIN_ZONE_SIDE_PX: int = 4
"""Smallest head-zone side, in pixels, that is still sampled for color."""

LABEL_VOTE_WEIGHT: float = 0.35
"""Weight of a detector-label vote. Labels are noisy: the detector often calls navy caps white."""

HSV_VOTE_WEIGHT: float = 0.65
"""Weight of a head-zone HSV vote. A confident cap color outweighs a contradicting label."""

GOALKEEPER_PANEL_MIN_SHARE: float = 0.08
"""Minimum share of head-zone pixels showing the goalkeeper color to flag goalkeeper evidence.

Lower than ``MIN_COLOR_PIXEL_SHARE`` because bi-colored goalkeeper caps only carry small red
panels or ear-guards on a team-color base.
"""

COLOR_ZONE_HORIZONTAL_INSET: float = 0.25
"""Share of the box width trimmed from each side when sampling cap color."""

COLOR_ZONE_TOP_FRACTION: float = 0.05
"""Share of the box height skipped at the top when sampling cap color."""

COLOR_ZONE_BOTTOM_FRACTION: float = 0.50
"""Share of the box height, from the top, where cap-color sampling stops.

Caps sit top-center in the detector box. Sampling only that region keeps face, skin and the
water at the box edges from diluting the vote (calibrated on Clip 1: 14/18 correct versus
10/18 for the full head zone).
"""

# --- Crop selection thresholds --------------------------------------------------------------
NEAR_EVENT_WINDOW_SEC: float = 1.0
"""Preferred half-window around the event time when ranking crop frames."""

MIN_NEAR_EVENT_OBSERVATIONS: int = 2
"""Fewer valid near-event observations than this triggers the full-window fallback."""

ASPECT_RATIO_TOLERANCE: float = 0.35
"""Maximum relative deviation of a box aspect ratio from the tracklet median."""

MIN_OBSERVATIONS_FOR_ASPECT_FILTER: int = 3
"""Aspect-ratio outlier rejection needs at least this many observations."""


@dataclass(frozen=True, slots=True)
class HsvRange:
    """Inclusive OpenCV HSV box. Wrapping hues (red) are expressed as two boxes."""

    hue_min: int
    hue_max: int
    sat_min: int
    sat_max: int
    val_min: int
    val_max: int


NAMED_COLOR_HSV_RANGES: dict[str, tuple[HsvRange, ...]] = {
    # Bright, unsaturated cap. Pool water is excluded by its saturation (>= ~100).
    "white": (HsvRange(0, 179, 0, 55, 105, 255),),
    # Royal blue above the water hue, plus the dark unsaturated navy/charcoal cap seen in dim
    # footage. Saturated dark water (saturation > 100) is excluded by the second range's cap.
    "blue": (HsvRange(112, 135, 70, 255, 25, 255), HsvRange(0, 179, 20, 100, 35, 82)),
    # Crimson/maroon panels at both ends of the hue circle. Skin is warm but weakly saturated
    # in dim footage (measured: only ~1% of face pixels reach saturation 90), so the floor of 100
    # plus the narrow hue band kept skin below a 0.02 red share on 20 real heads (panel
    # threshold: 0.08). Red panel recall could not be verified: no red cap was visible in Clip 1.
    "red": (HsvRange(168, 179, 100, 255, 55, 255), HsvRange(0, 5, 100, 255, 55, 255)),
    "black": (HsvRange(0, 179, 0, 255, 0, 45),),
    "yellow": (HsvRange(20, 35, 100, 255, 120, 255),),
    "green": (HsvRange(40, 85, 80, 255, 50, 255),),
}
"""Canonical color name to HSV ranges. ``navy`` shares the ``blue`` entry via aliases."""

COLOR_ALIASES: dict[str, str] = {
    "navy": "blue",
    "dark blue": "blue",
    "navy blue": "blue",
}

_SEPARATORS = re.compile(r"[-_]+")


def normalize_color_name(value: str | None) -> str:
    """Lower-case a color name and collapse separators; ``None`` becomes an empty string."""
    return " ".join(_SEPARATORS.sub(" ", (value or "").strip().lower()).split())


def canonical_color(value: str | None) -> str | None:
    """Return the canonical HSV-table color for ``value`` or ``None`` when it is unknown."""
    name = normalize_color_name(value)
    name = COLOR_ALIASES.get(name, name)
    return name if name in NAMED_COLOR_HSV_RANGES else None


def colors_match(first: str | None, second: str | None) -> bool:
    """Return True when two color names denote the same color.

    Known names are compared through the alias table; unknown names fall back to a
    normalized string comparison. Empty names never match.
    """
    left, right = normalize_color_name(first), normalize_color_name(second)
    if not left or not right:
        return False
    return (canonical_color(left) or left) == (canonical_color(right) or right)
