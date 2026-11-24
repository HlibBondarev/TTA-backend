"""Factory wiring concrete pipeline strategies from environment Settings."""

from __future__ import annotations

from video_analyzer.config import Settings
from video_analyzer.detectors.factory import DetectorFactory
from video_analyzer.media.ffmpeg_client import FFmpegClient
from video_analyzer.pass1.gemini_client import GeminiPass1Analyzer
from video_analyzer.pass2.cap_resolver import CapResolverFactory
from video_analyzer.pipeline.orchestrator import VideoAnalysisOrchestrator


def build_default_orchestrator(settings: Settings) -> VideoAnalysisOrchestrator:
    """Wire Pass 1, detector, BoT-SORT tracker, and cap-resolver strategies from settings."""
    ffmpeg_client = FFmpegClient(
        ffmpeg_bin=settings.ffmpeg_bin,
        ffprobe_bin=settings.ffprobe_bin,
    )
    pass1_analyzer = GeminiPass1Analyzer(
        api_key=settings.gemini_api_key,
        model_name=settings.gemini_pass1_model,
        timeout_sec=settings.gemini_request_timeout_sec,
    )
    detector = DetectorFactory.create(settings)
    cap_resolver = CapResolverFactory.create(settings)
    return VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=ffmpeg_client,
        pass1_analyzer=pass1_analyzer,
        detector=detector,
        cap_resolver=cap_resolver,
    )
