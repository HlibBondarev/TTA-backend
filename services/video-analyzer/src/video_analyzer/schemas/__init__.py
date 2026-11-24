"""Pydantic schemas for job requests, match snapshots, and draft outputs."""

from video_analyzer.schemas.draft import (
    CandidateLineup,
    DraftEvent,
    DraftPlayerPresence,
    DraftTimeAnchor,
    FoulImpact,
    ModelMetadata,
    PipelineDiagnostics,
    TimeAnchorType,
    VideoAnalysisDraft,
    VideoMetadata,
)
from video_analyzer.schemas.request import (
    AnalysisOptions,
    AnalyzeJobRequest,
    EventDefinitionSnapshot,
    LineupEntry,
    MatchSnapshot,
    SportConfigurationSnapshot,
    SportSnapshot,
    TeamSnapshot,
    VideoSource,
)

__all__ = [
    "AnalysisOptions",
    "AnalyzeJobRequest",
    "CandidateLineup",
    "DraftEvent",
    "DraftPlayerPresence",
    "DraftTimeAnchor",
    "EventDefinitionSnapshot",
    "FoulImpact",
    "LineupEntry",
    "MatchSnapshot",
    "ModelMetadata",
    "PipelineDiagnostics",
    "SportConfigurationSnapshot",
    "SportSnapshot",
    "TeamSnapshot",
    "TimeAnchorType",
    "VideoAnalysisDraft",
    "VideoMetadata",
    "VideoSource",
]
