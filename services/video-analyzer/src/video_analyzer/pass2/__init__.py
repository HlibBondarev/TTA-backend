"""Pass 2 targeted 4K tracking, cap crop selection, and lineup resolution."""

from video_analyzer.pass2.actor_matcher import ActorMatcher, MatchedTracklet
from video_analyzer.pass2.cap_resolver import (
    BaseCapResolver,
    CapResolutionResult,
    CapResolverFactory,
    GeminiCropGridCapResolver,
    LocalOcrCapResolver,
)
from video_analyzer.pass2.crop_selector import CropCandidate, CropSelector, SelectedCrops
from video_analyzer.pass2.lineup_matcher import LineupMatcher, LineupMatchOutcome
from video_analyzer.pass2.tracker import BoTSortTracker, Tracklet, TrackletObservation

__all__ = [
    "ActorMatcher",
    "BaseCapResolver",
    "BoTSortTracker",
    "CapResolutionResult",
    "CapResolverFactory",
    "CropCandidate",
    "CropSelector",
    "GeminiCropGridCapResolver",
    "LineupMatchOutcome",
    "LineupMatcher",
    "LocalOcrCapResolver",
    "MatchedTracklet",
    "SelectedCrops",
    "Tracklet",
    "TrackletObservation",
]
