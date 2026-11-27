"""Sport-agnostic analysis core plus pluggable per-sport profiles."""

from video_analyzer.sport.context import AnalysisContext, build_analysis_context
from video_analyzer.sport.events import EventDefinition, build_event_catalog
from video_analyzer.sport.profile import (
    DefaultProfile,
    DefinitionKey,
    Pass1PromptText,
    PromptRuleBlock,
    SportProfile,
    WaterPoloProfile,
    definition_key,
    resolve_sport_profile,
)

__all__ = [
    "AnalysisContext",
    "DefaultProfile",
    "DefinitionKey",
    "EventDefinition",
    "Pass1PromptText",
    "PromptRuleBlock",
    "SportProfile",
    "WaterPoloProfile",
    "build_analysis_context",
    "build_event_catalog",
    "definition_key",
    "resolve_sport_profile",
]
