"""Factory wiring concrete pipeline strategies from environment Settings."""

from __future__ import annotations

import importlib.util

from video_analyzer.config import Settings
from video_analyzer.detectors.factory import DetectorFactory
from video_analyzer.gemini.resilience import GeminiResilience
from video_analyzer.media.ffmpeg_client import FFmpegClient
from video_analyzer.pass1.gemini_client import GeminiPass1Analyzer
from video_analyzer.pass2.cap_resolver import (
    BaseCapResolver,
    CapResolverFactory,
    LocalOcrCapResolver,
)
from video_analyzer.pipeline.orchestrator import VideoAnalysisOrchestrator
from video_analyzer.verifier.base import BaseEventVerifier
from video_analyzer.verifier.gemini_verifier import GeminiClipVerifier


def build_fallback_cap_resolver(settings: Settings) -> BaseCapResolver | None:
    """Return a local OCR resolver to use if the Gemini Pass 2 breaker opens, when available.

    The fallback only makes sense when Gemini is the primary resolver and the optional
    ``easyocr`` dependency is installed; otherwise remaining events are marked explicitly.
    """
    if settings.cap_resolver_provider != "gemini_crop_grid":
        return None
    if importlib.util.find_spec("easyocr") is None:
        return None
    return LocalOcrCapResolver(languages=settings.ocr_languages)


def build_verifier(settings: Settings, ffmpeg_client: FFmpegClient) -> BaseEventVerifier | None:
    """Return the clip verifier, or None when ``VERIFIER_ENABLED`` is false."""
    if not settings.verifier_enabled:
        return None
    return GeminiClipVerifier(
        api_key=settings.gemini_api_key,
        ffmpeg_client=ffmpeg_client,
        model_name=settings.gemini_verifier_model,
        timeout_sec=settings.gemini_request_timeout_sec,
        thinking_level=settings.gemini_verifier_thinking_level,
        resilience=GeminiResilience.from_settings(settings, "verifier"),
        max_candidates=settings.verifier_max_candidates,
        batch_size=settings.verifier_batch_size,
        max_concurrency=settings.verifier_max_concurrency,
    )


def build_default_orchestrator(settings: Settings) -> VideoAnalysisOrchestrator:
    """Wire Pass 1, verifier, detector, BoT-SORT tracker and cap-resolver from settings."""
    ffmpeg_client = FFmpegClient(
        ffmpeg_bin=settings.ffmpeg_bin,
        ffprobe_bin=settings.ffprobe_bin,
        hwaccel=settings.ffmpeg_hwaccel,
        video_encoder=settings.ffmpeg_video_encoder,
        extraction_threads=settings.ffmpeg_extraction_threads,
        extraction_hwaccel=settings.ffmpeg_extraction_hwaccel,
    )
    pass1_analyzer = GeminiPass1Analyzer(
        api_key=settings.gemini_api_key,
        model_name=settings.gemini_pass1_model,
        timeout_sec=settings.gemini_request_timeout_sec,
        thinking_level=settings.gemini_pass1_thinking_level,
        resilience=GeminiResilience.from_settings(settings, "pass1"),
    )
    detector = DetectorFactory.create(settings)
    cap_resolver = CapResolverFactory.create(
        settings, resilience=GeminiResilience.from_settings(settings, "pass2")
    )
    return VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=ffmpeg_client,
        pass1_analyzer=pass1_analyzer,
        detector=detector,
        cap_resolver=cap_resolver,
        fallback_cap_resolver=build_fallback_cap_resolver(settings),
        verifier=build_verifier(settings, ffmpeg_client),
    )
