"""Sport-agnostic analysis core plus pluggable per-sport profiles."""

from video_analyzer.sport.context import AnalysisContext, build_analysis_context
from video_analyzer.sport.events import EventDefinition, build_event_catalog
from video_analyzer.sport.profile import (
    DefaultProfile,
    Pass1PromptText,
    SportProfile,
    WaterPoloProfile,
    resolve_sport_profile,
)

__all__ = [
    "AnalysisContext",
    "DefaultProfile",
    "EventDefinition",
    "Pass1PromptText",
    "SportProfile",
    "WaterPoloProfile",
    "build_analysis_context",
    "build_event_catalog",
    "resolve_sport_profile",
]
