"""End-to-end video analysis orchestrator (Pass 1, verification, Pass 2)."""

from __future__ import annotations

import logging
import re
import tempfile
import threading
import time
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from uuid import uuid4

from video_analyzer.config import Settings
from video_analyzer.detectors.base import BaseObjectDetector
from video_analyzer.detectors.concurrency import ConcurrencyLimitedDetector, TimedDetector
from video_analyzer.gemini.resilience import (
    BreakerReason,
    GeminiCallBlockedError,
    GeminiJobScope,
    job_scope,
)
from video_analyzer.media.ffmpeg_client import (
    ExtractedFrame,
    FFmpegCancelledError,
    FFmpegClient,
    VideoChunk,
    VideoProbeResult,
)
from video_analyzer.pass1.anchor_reconciler import AnchorReconciler
from video_analyzer.pass1.chunk_time import normalize_chunk_result
from video_analyzer.pass1.dead_time_filter import filter_dead_time_events
from video_analyzer.pass1.deduplicator import DeduplicationResult, EventDeduplicator
from video_analyzer.pass1.gemini_client import BasePass1Analyzer
from video_analyzer.pass1.models import (
    Pass1AnchorCandidate,
    Pass1ChunkResult,
    Pass1EventCandidate,
)
from video_analyzer.pass2.actor_matcher import (
    MIN_BALL_DETECTIONS,
    ActorMatcher,
    ActorMatchResult,
    MatchedTracklet,
)
from video_analyzer.pass2.cap_resolver import BaseCapResolver, CapResolutionResult
from video_analyzer.pass2.color_constants import colors_match, normalize_color_name
from video_analyzer.pass2.crop_filters import CropSelectionContext
from video_analyzer.pass2.crop_selector import CropSelector, SelectedCrops
from video_analyzer.pass2.debug_artifacts import (
    Pass2Counters,
    Pass2DebugRecord,
    build_event_payload,
    chosen_bbox_on_frame,
    nearest_frame,
    write_detection_overlay,
    write_event_json,
    write_summary_json,
)
from video_analyzer.pass2.failure_reasons import (
    REVIEW_OCR_CAP_COLOR_MISMATCH,
    Pass2FailureReason,
    failure_reason_for_breaker,
)
from video_analyzer.pass2.lineup_matcher import ActorEvidence, LineupMatcher, LineupMatchOutcome
from video_analyzer.pass2.team_classifier import TeamClass
from video_analyzer.pass2.tracker import BoTSortTracker, TrackingResult
from video_analyzer.pipeline.parallel import ordered_parallel_map
from video_analyzer.pipeline.timestamp_aligner import (
    resolve_video_start_utc,
    synthesize_closed_presences,
    to_utc_timestamp,
)
from video_analyzer.schemas.draft import (
    DraftEvent,
    DraftTimeAnchor,
    GeminiTokenUsage,
    ModelMetadata,
    PipelineDiagnostics,
    VideoAnalysisDraft,
    VideoMetadata,
)
from video_analyzer.schemas.request import AnalyzeJobRequest
from video_analyzer.sport.context import AnalysisContext, build_analysis_context
from video_analyzer.usage import build_gemini_usage_diagnostics
from video_analyzer.verifier.apply import mark_anchor_unverified, mark_event_unverified
from video_analyzer.verifier.base import BaseEventVerifier
from video_analyzer.verifier.models import VerificationResult

logger = logging.getLogger(__name__)
_CAP_COLOR_HINT_MISMATCH_REASON = "CAP_COLOR_HINT_MISMATCH"
_GEMINI_CAP_RESOLVER_PROVIDER = "gemini_crop_grid"

_PASS1_PROGRESS_BASE = 15
_PASS1_PROGRESS_SPAN = 35
_VERIFICATION_PROGRESS = 52
_PASS1_CHUNKS_DIRNAME = "pass1_chunks"
# Pass 2 shares one progress range: Phase A (3 units per event) and Phase B (1 unit per event)
# interleave batch by batch, so a single monotonic counter covers both.
_PASS2_PROGRESS_BASE = 55
_PASS2_PROGRESS_SPAN = 40
_PHASE_A_PROGRESS_UNITS = 3
_PHASE_B_PROGRESS_UNITS = 1


class JobCancelledError(RuntimeError):
    """Raised when an in-flight video analysis job is cancelled cooperatively."""


@dataclass(slots=True)
class _PhaseATimings:
    """Cumulative seconds spent in each Phase A sub-stage for one event."""

    extraction_sec: float = 0.0
    detection_sec: float = 0.0
    tracking_sec: float = 0.0
    crop_selection_sec: float = 0.0

    @property
    def total_sec(self) -> float:
        return (
            self.extraction_sec + self.detection_sec + self.tracking_sec + self.crop_selection_sec
        )


@dataclass(slots=True)
class _PreparedEvent:
    """Phase A output for one event: selected cap crops (or why there are none)."""

    event_index: int
    candidate: Pass1EventCandidate
    has_spatial_hint: bool
    selected_crops: SelectedCrops | None = None
    failure_reason: Pass2FailureReason | None = None
    actor_match_tied: bool = False
    cap_ambiguous: bool = False
    timings: _PhaseATimings = field(default_factory=_PhaseATimings)
    warnings: list[str] = field(default_factory=list)
    # Per-frame ball/goal detections feed the soft ball-proximity bonus and debug counters.
    tracking: TrackingResult | None = None
    actor_match_score: float | None = None
    actor_team_class: TeamClass | None = None
    match_result: ActorMatchResult | None = None
    matched_summary: dict[str, object] | None = None
    crop_report: list[dict[str, object]] = field(default_factory=list)


@dataclass(slots=True)
class _CapOutcome:
    """Phase B output for one event."""

    cap_result: CapResolutionResult | None = None
    token_usage: GeminiTokenUsage | None = None
    failure_reason: Pass2FailureReason | None = None
    elapsed_sec: float = 0.0
    used_fallback: bool = False
    blocked_by_breaker: bool = False
    warnings: list[str] = field(default_factory=list)


@dataclass(frozen=True, slots=True)
class _Pass2Context:
    """Read-only job inputs shared by every Pass 2 task."""

    video_path: Path
    probe: VideoProbeResult
    request: AnalyzeJobRequest
    analysis: AnalysisContext
    allowed_caps: list[int]
    cap_color_hint: str | None
    cancel_event: threading.Event | None
    debug_dir: Path | None


@dataclass(slots=True)
class _Pass2Result:
    """Everything Pass 2 contributes to the final draft."""

    events: list[DraftEvent] = field(default_factory=list)
    windows_processed: int = 0
    timings: dict[str, float] = field(default_factory=dict)
    usages: list[GeminiTokenUsage | None] = field(default_factory=list)

    @property
    def resolved_count(self) -> int:
        return sum(1 for event in self.events if event.match_lineup_id is not None)

    @property
    def unresolved_count(self) -> int:
        return len(self.events) - self.resolved_count


class _CapBreakerState:
    """Phase B state shared across tasks: remembers that the Gemini breaker has opened."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._reason: BreakerReason | None = None
        self._detail = ""
        self.fallback_lock = threading.Lock()

    @property
    def reason(self) -> BreakerReason | None:
        return self._reason

    @property
    def detail(self) -> str:
        return self._detail

    def block(self, reason: BreakerReason, detail: str = "") -> None:
        with self._lock:
            if self._reason is None:
                self._reason = reason
                self._detail = detail


class _ProgressTracker:
    """Thread-safe progress reporter; each completed task advances ``total`` by some units."""

    def __init__(
        self,
        callback: Callable[[str, int], None] | None,
        stage: str,
        total: int,
        base: int,
        span: int,
    ) -> None:
        self._callback = callback
        self._stage = stage
        self._total = max(1, total)
        self._base = base
        self._span = span
        self._done = 0
        self._lock = threading.Lock()

    def advance(self, stage: str | None = None, units: int = 1) -> None:
        """Record ``units`` of finished work, optionally reporting under a different ``stage``."""
        if self._callback is None:
            return
        # Percentage computation and delivery share one critical section so concurrent workers
        # can never emit updates out of order (e.g. 60 % arriving after 65 %).
        with self._lock:
            self._done += units
            pct = self._base + int((self._done / self._total) * self._span)
            self._callback(stage or self._stage, pct)


class VideoAnalysisOrchestrator:
    """Coordinates Pass 1 (VLM), clip verification and Pass 2 (4K tracking + cap resolution)."""

    def __init__(
        self,
        settings: Settings,
        ffmpeg_client: FFmpegClient,
        pass1_analyzer: BasePass1Analyzer,
        detector: BaseObjectDetector,
        cap_resolver: BaseCapResolver,
        deduplicator: EventDeduplicator | None = None,
        anchor_reconciler: AnchorReconciler | None = None,
        tracker: BoTSortTracker | None = None,
        actor_matcher: ActorMatcher | None = None,
        crop_selector: CropSelector | None = None,
        lineup_matcher: LineupMatcher | None = None,
        fallback_cap_resolver: BaseCapResolver | None = None,
        verifier: BaseEventVerifier | None = None,
    ) -> None:
        self._settings = settings
        self._ffmpeg = ffmpeg_client
        self._pass1 = pass1_analyzer
        self._detector = self._limit_detector_concurrency(detector, settings)
        self._cap_resolver = cap_resolver
        self._fallback_cap_resolver = fallback_cap_resolver
        self._verifier = verifier
        self._deduplicator = deduplicator or EventDeduplicator()
        self._anchor_reconciler = anchor_reconciler or AnchorReconciler()
        self._tracker = tracker or BoTSortTracker()
        self._actor_matcher = actor_matcher or ActorMatcher()
        self._crop_selector = crop_selector or CropSelector()
        self._lineup_matcher = lineup_matcher or LineupMatcher()
        # One semaphore for ALL Phase A tasks: bounds simultaneous 4K ffmpeg decoders (RAM/CPU).
        self._extraction_slots = threading.BoundedSemaphore(
            settings.ffmpeg_extraction_max_concurrency
        )

    @contextmanager
    def _extraction_slot(self, cancel_event: threading.Event | None) -> Iterator[None]:
        """Hold one 4K extraction slot, staying responsive to cancellation while waiting."""
        while not self._extraction_slots.acquire(timeout=0.1):
            self._check_cancelled(cancel_event)
        try:
            yield
        finally:
            self._extraction_slots.release()

    @staticmethod
    def _limit_detector_concurrency(
        detector: BaseObjectDetector,
        settings: Settings,
    ) -> BaseObjectDetector:
        """Wrap the detector in ONE global limiter shared by all Phase A tasks.

        Remote providers may overlap up to ``DETECTOR_MAX_CONCURRENCY`` calls; local providers
        (ONNX/YOLO) are serialized.
        """
        if isinstance(detector, ConcurrencyLimitedDetector):
            return detector
        limit = settings.detector_max_concurrency if detector.supports_concurrent_inference else 1
        return ConcurrencyLimitedDetector(detector, limit)

    def run(
        self,
        request: AnalyzeJobRequest,
        cancel_event: threading.Event | None = None,
        progress_callback: Callable[[str, int], None] | None = None,
        debug_dir: Path | None = None,
    ) -> VideoAnalysisDraft:
        """Execute the full two-pass analysis and return a TTA-compatible draft."""
        scope = GeminiJobScope(
            max_calls=self._settings.gemini_max_calls_per_job,
            max_cost_usd=self._settings.gemini_max_estimated_cost_usd,
        )
        with job_scope(scope):
            try:
                return self._run_job(request, cancel_event, progress_callback, debug_dir, scope)
            except FFmpegCancelledError as exc:
                raise JobCancelledError(str(exc)) from exc

    def _run_job(
        self,
        request: AnalyzeJobRequest,
        cancel_event: threading.Event | None,
        progress_callback: Callable[[str, int], None] | None,
        debug_dir: Path | None,
        scope: GeminiJobScope,
    ) -> VideoAnalysisDraft:
        t_start = time.monotonic()
        timings: dict[str, float] = {}
        warnings: list[str] = []

        with tempfile.TemporaryDirectory(
            prefix=f"tta_va_{request.job_id}_",
            dir=self._settings.work_dir,
            ignore_cleanup_errors=True,
        ) as tmp_root:
            work_dir = Path(tmp_root)
            self._report(progress_callback, "probing_video", 5)
            self._check_cancelled(cancel_event)

            t_probe = time.monotonic()
            video_path = self._ffmpeg.resolve_source_path(
                request.video_source,
                work_dir,
                cancel_event=cancel_event,
            )
            probe = self._ffmpeg.probe(video_path, cancel_event=cancel_event)
            timings["probe"] = round(time.monotonic() - t_probe, 3)

            video_start_utc, start_source, start_warnings = resolve_video_start_utc(
                requested_start_utc=request.video_start_utc,
                duration_sec=probe.duration_sec,
            )
            warnings.extend(start_warnings)

            analysis = build_analysis_context(request.match_snapshot, request.runtime_hints)
            chunk_results, pass1_timings, normalize_warnings = self._run_pass1(
                video_path=video_path,
                probe=probe,
                request=request,
                analysis=analysis,
                work_dir=work_dir,
                cancel_event=cancel_event,
                progress_callback=progress_callback,
            )
            timings.update(pass1_timings)
            warnings.extend(normalize_warnings)

            dedup = self._deduplicator.process_detailed(
                chunk_results=chunk_results,
                event_definitions=analysis.event_catalog,
                total_duration_sec=probe.duration_sec,
                min_confidence=request.analysis_options.min_event_confidence,
                profile=analysis.profile,
            )
            warnings.extend(dedup.warnings)

            anchor_cands, anchor_warnings = self._anchor_reconciler.reconcile(
                chunk_results=chunk_results,
                total_duration_sec=probe.duration_sec,
            )
            warnings.extend(anchor_warnings)

            verification, verifier_sec = self._run_verification(
                analysis=analysis,
                dedup=dedup,
                anchors=anchor_cands,
                mezzanine_path=FFmpegClient.mezzanine_path(work_dir / _PASS1_CHUNKS_DIRNAME),
                work_dir=work_dir,
                cancel_event=cancel_event,
                progress_callback=progress_callback,
            )
            timings["pass1Verification"] = verifier_sec
            warnings.extend(verification.warnings)

            events_cand, dead_time_warnings = filter_dead_time_events(
                verification.events, verification.anchors
            )
            warnings.extend(dead_time_warnings)
            draft_anchors = self._to_draft_anchors(verification.anchors, video_start_utc)

            pass2 = self._run_pass2(
                video_path=video_path,
                probe=probe,
                request=request,
                analysis=analysis,
                candidates=events_cand,
                video_start_utc=video_start_utc,
                cancel_event=cancel_event,
                progress_callback=progress_callback,
                debug_dir=debug_dir,
                warnings=warnings,
            )
            timings.update(pass2.timings)
            warnings.extend(self._breaker_warnings(scope))

            gemini_usage = build_gemini_usage_diagnostics(
                pass1_model=self._settings.gemini_pass1_model,
                pass1_usages=[res.token_usage for _, res in chunk_results],
                pass2_model=self._settings.gemini_pass2_model,
                pass2_usages=pass2.usages,
                pricing_table=self._settings.resolved_gemini_model_pricing,
                verifier_model=self._settings.gemini_verifier_model,
                verifier_usages=verification.usages,
            )
            presences = synthesize_closed_presences(
                events=pass2.events,
                anchors=draft_anchors,
                video_start_utc=video_start_utc,
                enabled=request.analysis_options.generate_player_presences,
            )

            timings["total"] = round(time.monotonic() - t_start, 3)
            detector_info = self._detector.provider_info()

            self._check_cancelled(cancel_event)
            self._report(progress_callback, "completed", 100)
            return VideoAnalysisDraft(
                schema_version="1.0",
                job_id=request.job_id,
                match_id=request.match_id,
                tracked_team_id=request.tracked_team_id,
                status="completed",
                video_metadata=VideoMetadata(
                    duration_sec=round(probe.duration_sec, 3),
                    width=probe.width,
                    height=probe.height,
                    fps=probe.fps,
                    has_audio=probe.has_audio,
                    video_start_utc=video_start_utc,
                    video_start_utc_source=start_source,
                ),
                model_metadata=self._build_model_metadata(request, detector_info),
                time_anchors=draft_anchors,
                events=pass2.events,
                player_presences=presences,
                diagnostics=PipelineDiagnostics(
                    pass1_chunks_processed=len(chunk_results),
                    pass1_raw_candidate_count=dedup.raw_count,
                    deduplicated_event_count=len(dedup.candidates),
                    pass2_windows_processed=pass2.windows_processed,
                    resolved_actor_count=pass2.resolved_count,
                    unresolved_actor_count=pass2.unresolved_count,
                    warnings=warnings,
                    timings_sec=timings,
                    gemini_usage=gemini_usage,
                ),
            )

    def _build_model_metadata(
        self,
        request: AnalyzeJobRequest,
        detector_info: dict[str, str],
    ) -> ModelMetadata:
        settings = self._settings
        uses_gemini_cap_model = (
            settings.cap_resolver_provider == _GEMINI_CAP_RESOLVER_PROVIDER
            and not request.analysis_options.skip_pass2
        )
        return ModelMetadata(
            pass1_vlm_model=settings.gemini_pass1_model,
            detector_provider=detector_info.get("provider", settings.detector_provider),
            detector_model=detector_info.get("model", settings.detector_model_path_or_id),
            tracker=settings.tracker_type,
            cap_resolver=(
                "skipped" if request.analysis_options.skip_pass2 else settings.cap_resolver_provider
            ),
            pass2_cap_model=settings.gemini_pass2_model if uses_gemini_cap_model else None,
            verifier_model=settings.gemini_verifier_model,
        )

    @staticmethod
    def _to_draft_anchors(
        anchor_cands: list[Pass1AnchorCandidate],
        video_start_utc: datetime,
    ) -> list[DraftTimeAnchor]:
        return [
            DraftTimeAnchor(
                id=uuid4(),
                period_number=a.period_number,
                type=a.type,
                video_time_sec=a.video_time_sec,
                timestamp=to_utc_timestamp(video_start_utc, a.video_time_sec),
                confidence=round(a.confidence, 4),
                needs_review=a.needs_review,
                evidence=a.evidence,
            )
            for a in anchor_cands
        ]

    def _run_verification(
        self,
        analysis: AnalysisContext,
        dedup: DeduplicationResult,
        anchors: list[Pass1AnchorCandidate],
        mezzanine_path: Path,
        work_dir: Path,
        cancel_event: threading.Event | None,
        progress_callback: Callable[[str, int], None] | None,
    ) -> tuple[VerificationResult, float]:
        """Run the clip verifier; any non-cancellation failure keeps candidates, flagged."""
        unchanged = VerificationResult(events=list(dedup.candidates), anchors=list(anchors))
        if self._verifier is None or not (dedup.candidates or anchors):
            return unchanged, 0.0
        self._report(progress_callback, "pass1_verification", _VERIFICATION_PROGRESS)
        self._check_cancelled(cancel_event)
        started = time.monotonic()
        try:
            result = self._verifier.verify(
                context=analysis,
                events=dedup.candidates,
                anchors=anchors,
                mezzanine_path=mezzanine_path,
                work_dir=work_dir,
                conflict_groups=dedup.conflict_groups,
                cancel_event=cancel_event,
            )
        except FFmpegCancelledError:
            raise
        except Exception as exc:
            logger.warning("Candidate verification failed; keeping candidates unverified: %s", exc)
            for event in unchanged.events:
                mark_event_unverified(event)
            for anchor in unchanged.anchors:
                mark_anchor_unverified(anchor)
            unchanged.warnings.append(f"Candidate verification failed open: {exc}")
            return unchanged, round(time.monotonic() - started, 3)
        return result, round(time.monotonic() - started, 3)

    @staticmethod
    def _breaker_warnings(scope: GeminiJobScope) -> list[str]:
        return [
            f"Gemini {breaker.name} circuit breaker opened ({breaker.reason}): {breaker.detail}"
            for breaker in scope.open_breakers()
        ]

    # ------------------------------------------------------------------ Pass 1

    def _run_pass1(
        self,
        video_path: Path,
        probe: VideoProbeResult,
        request: AnalyzeJobRequest,
        analysis: AnalysisContext,
        work_dir: Path,
        cancel_event: threading.Event | None,
        progress_callback: Callable[[str, int], None] | None,
    ) -> tuple[list[tuple[VideoChunk, Pass1ChunkResult]], dict[str, float], list[str]]:
        opts = request.analysis_options
        self._report(progress_callback, "pass1_chunking", 10)
        chunk_timings: dict[str, float] = {}
        t_chunk = time.monotonic()
        chunks = self._ffmpeg.create_pass1_chunks(
            video_path=video_path,
            probe=probe,
            output_dir=work_dir / _PASS1_CHUNKS_DIRNAME,
            chunk_duration_sec=opts.pass1_chunk_duration_sec,
            overlap_sec=opts.pass1_overlap_sec,
            target_height=opts.pass1_target_height,
            mezzanine_fps=opts.mezzanine_fps,
            cancel_event=cancel_event,
            timings=chunk_timings,
        )
        chunk_sec = round(time.monotonic() - t_chunk, 3)

        t_vlm = time.monotonic()
        progress = _ProgressTracker(
            progress_callback,
            "pass1_vlm",
            len(chunks),
            _PASS1_PROGRESS_BASE,
            _PASS1_PROGRESS_SPAN,
        )

        def analyze(chunk: VideoChunk) -> tuple[VideoChunk, Pass1ChunkResult]:
            result = self._pass1.analyze_chunk(analysis, chunk)
            progress.advance()
            return chunk, result

        # Results are merged by chunk index (input order), so output is deterministic and
        # PASS1_MAX_CONCURRENCY=1 reproduces the sequential behavior exactly.
        chunk_results = ordered_parallel_map(
            analyze,
            chunks,
            self._settings.pass1_max_concurrency,
            before_task=lambda: self._check_cancelled(cancel_event),
        )
        vlm_sec = round(time.monotonic() - t_vlm, 3)
        chunk_results, normalize_warnings = self._normalize_chunk_results(chunk_results)
        return (
            chunk_results,
            {
                "pass1TranscodeAndChunk": chunk_sec,
                "pass1Mezzanine": chunk_timings.get("pass1Mezzanine", 0.0),
                "pass1ChunkSlicing": chunk_timings.get("pass1ChunkSlicing", 0.0),
                "pass1Vlm": vlm_sec,
            },
            normalize_warnings,
        )

    @staticmethod
    def _normalize_chunk_results(
        chunk_results: list[tuple[VideoChunk, Pass1ChunkResult]],
    ) -> tuple[list[tuple[VideoChunk, Pass1ChunkResult]], list[str]]:
        """Convert global-timeline offsets to chunk-relative ones before dedup and anchors."""
        normalized: list[tuple[VideoChunk, Pass1ChunkResult]] = []
        warnings: list[str] = []
        for chunk, result in chunk_results:
            fixed, chunk_warnings = normalize_chunk_result(chunk, result)
            normalized.append((chunk, fixed))
            warnings.extend(chunk_warnings)
        return normalized, warnings

    # ------------------------------------------------------------------ Pass 2

    def _run_pass2(
        self,
        video_path: Path,
        probe: VideoProbeResult,
        request: AnalyzeJobRequest,
        analysis: AnalysisContext,
        candidates: list[Pass1EventCandidate],
        video_start_utc: datetime,
        cancel_event: threading.Event | None,
        progress_callback: Callable[[str, int], None] | None,
        debug_dir: Path | None,
        warnings: list[str],
    ) -> _Pass2Result:
        ctx = _Pass2Context(
            video_path=video_path,
            probe=probe,
            request=request,
            analysis=analysis,
            allowed_caps=sorted({entry.number for entry in analysis.lineup}),
            cap_color_hint=analysis.tracked_cap_color_hint,
            cancel_event=cancel_event,
            debug_dir=debug_dir,
        )
        result = _Pass2Result()
        if request.analysis_options.skip_pass2:
            prepared = [self._skipped_event(i, cand) for i, cand in enumerate(candidates, 1)]
            outcomes = [_CapOutcome() for _ in prepared]
        else:
            prepared, outcomes = self._run_pass2_batches(candidates, ctx, progress_callback)
            result.windows_processed = len(prepared)

        counters = Pass2Counters()
        for item, outcome in zip(prepared, outcomes, strict=True):
            warnings.extend(item.warnings)
            warnings.extend(outcome.warnings)
            if outcome.token_usage is not None:
                result.usages.append(outcome.token_usage)
            result.events.append(self._finish_event(item, outcome, ctx, video_start_utc, counters))
        warnings.extend(self._attribution_summary(counters, debug_dir))
        warnings.extend(self._fallback_summary(outcomes, result.events))
        result.timings = self._pass2_timings(prepared, outcomes)
        return result

    @staticmethod
    def _skipped_event(event_index: int, candidate: Pass1EventCandidate) -> _PreparedEvent:
        return _PreparedEvent(
            event_index=event_index,
            candidate=candidate,
            has_spatial_hint=candidate.actor_bbox_normalized is not None,
        )

    @staticmethod
    def _pass2_timings(
        prepared: list[_PreparedEvent],
        outcomes: list[_CapOutcome],
    ) -> dict[str, float]:
        """Cumulative per-stage seconds across events (can exceed wall-clock when parallel)."""
        cap_sec = sum(outcome.elapsed_sec for outcome in outcomes)
        return {
            "pass2ExtractAndTrack": round(sum(p.timings.total_sec for p in prepared), 3),
            "pass2CapResolution": round(cap_sec, 3),
            "pass2FrameExtraction": round(sum(p.timings.extraction_sec for p in prepared), 3),
            "pass2DetectorInference": round(sum(p.timings.detection_sec for p in prepared), 3),
            "pass2Tracking": round(sum(p.timings.tracking_sec for p in prepared), 3),
            "pass2CropSelection": round(sum(p.timings.crop_selection_sec for p in prepared), 3),
            "pass2CapOcr": round(cap_sec, 3),
        }

    def _run_pass2_batches(
        self,
        candidates: list[Pass1EventCandidate],
        ctx: _Pass2Context,
        progress_callback: Callable[[str, int], None] | None,
    ) -> tuple[list[_PreparedEvent], list[_CapOutcome]]:
        """Run Phase A then Phase B over bounded batches of events.

        Every event still goes through Phase A (tracking, crop selection) before Phase B (cap
        resolution), but only ``PASS2_BATCH_SIZE`` crop bundles are alive at once, so peak memory
        no longer grows with match length. The breaker state is shared by all batches, so a
        tripped Gemini breaker keeps affecting the remaining events.
        """
        batch_size = self._settings.pass2_batch_size
        progress = _ProgressTracker(
            progress_callback,
            "pass2_tracking",
            len(candidates) * (_PHASE_A_PROGRESS_UNITS + _PHASE_B_PROGRESS_UNITS),
            _PASS2_PROGRESS_BASE,
            _PASS2_PROGRESS_SPAN,
        )
        state = _CapBreakerState()
        prepared: list[_PreparedEvent] = []
        outcomes: list[_CapOutcome] = []
        for offset in range(0, len(candidates), batch_size):
            batch = self._run_phase_a(
                candidates[offset : offset + batch_size], ctx, progress, first_index=offset + 1
            )
            outcomes.extend(self._run_phase_b(batch, ctx, state, progress))
            prepared.extend(batch)
        return prepared, outcomes

    # -------------------------------------------------------- Pass 2 / Phase A

    def _run_phase_a(
        self,
        candidates: list[Pass1EventCandidate],
        ctx: _Pass2Context,
        progress: _ProgressTracker,
        first_index: int = 1,
    ) -> list[_PreparedEvent]:
        """Extract frames, detect, track and select crops for every event of one batch."""

        def prepare(indexed: tuple[int, Pass1EventCandidate]) -> _PreparedEvent:
            prepared = self._prepare_event(indexed[0], indexed[1], ctx)
            progress.advance("pass2_tracking", _PHASE_A_PROGRESS_UNITS)
            return prepared

        return ordered_parallel_map(
            prepare,
            list(enumerate(candidates, start=first_index)),
            self._settings.pass2_phase_a_max_concurrency,
            before_task=lambda: self._check_cancelled(ctx.cancel_event),
        )

    def _prepare_event(
        self,
        event_index: int,
        candidate: Pass1EventCandidate,
        ctx: _Pass2Context,
    ) -> _PreparedEvent:
        prepared = self._skipped_event(event_index, candidate)
        frames = self._extract_frames(prepared, ctx)
        if not frames:
            prepared.failure_reason = Pass2FailureReason.NO_FRAMES_EXTRACTED
            prepared.warnings.append(
                f"Event #{event_index} ({candidate.definition.short_name} at "
                f"{candidate.video_time_sec}s): no frames extracted in Pass 2 window."
            )
            return prepared

        matched = self._track_and_match(prepared, frames, ctx)
        if matched is None:
            match_result = prepared.match_result
            prepared.failure_reason = (
                match_result.failure_reason if match_result else None
            ) or Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED
        else:
            self._select_crops(prepared, matched, frames, ctx)
        self._write_detection_overlay(prepared, frames, matched, ctx)
        return prepared

    @staticmethod
    def _write_detection_overlay(
        prepared: _PreparedEvent,
        frames: list[ExtractedFrame],
        matched: MatchedTracklet | None,
        ctx: _Pass2Context,
    ) -> None:
        """Write the all-detections overlay, also for unmatched and no-crop events."""
        frame = nearest_frame(frames, prepared.candidate.video_time_sec)
        if ctx.debug_dir is None or frame is None or prepared.tracking is None:
            return
        write_detection_overlay(
            debug_dir=ctx.debug_dir,
            event_idx=prepared.event_index,
            frame=frame,
            tracking=prepared.tracking,
            target_bbox_normalized=prepared.candidate.actor_bbox_normalized,
            chosen_bbox=chosen_bbox_on_frame(matched.tracklet if matched else None, frame.index),
        )

    def _extract_frames(self, prepared: _PreparedEvent, ctx: _Pass2Context) -> list[ExtractedFrame]:
        opts = ctx.request.analysis_options
        with self._extraction_slot(ctx.cancel_event):
            # Timed inside the slot so queueing behind other windows is not counted as decoding.
            started = time.monotonic()
            frames = self._ffmpeg.extract_window_frames(
                video_path=ctx.video_path,
                center_sec=prepared.candidate.video_time_sec,
                half_window_sec=opts.pass2_window_half_sec,
                total_duration_sec=ctx.probe.duration_sec,
                sample_fps=opts.pass2_sample_fps,
                frame_width=ctx.probe.width,
                frame_height=ctx.probe.height,
                video_stream_index=ctx.probe.video_stream_index,
                detector_input_width=self._settings.detector_input_width,
                cancel_event=ctx.cancel_event,
            )
            prepared.timings.extraction_sec = time.monotonic() - started
        return frames

    def _track_and_match(
        self,
        prepared: _PreparedEvent,
        frames: list[ExtractedFrame],
        ctx: _Pass2Context,
    ) -> MatchedTracklet | None:
        timed_detector = TimedDetector(self._detector)
        started = time.monotonic()
        tracking = self._tracker.track_window_with_scene(
            frames,
            timed_detector,
            ctx.analysis.profile.cap_zone_geometry,
            ctx.analysis.profile.max_balls_per_frame,
        )
        elapsed = time.monotonic() - started
        prepared.tracking = tracking
        prepared.timings.detection_sec = timed_detector.elapsed_sec
        prepared.timings.tracking_sec = max(0.0, elapsed - timed_detector.elapsed_sec)

        match_result = self._actor_matcher.match_detailed(
            candidate=prepared.candidate,
            tracklets=tracking.tracklets,
            context=ctx.analysis,
            frame_width=frames[0].native_width,
            frame_height=frames[0].native_height,
            frames=frames,
            ball_detections=tracking.ball_detections,
        )
        prepared.match_result = match_result
        matched = match_result.matched
        if matched is not None:
            prepared.has_spatial_hint = matched.has_spatial_hint
            prepared.actor_match_tied = matched.is_tied
            prepared.actor_match_score = matched.match_score
            prepared.actor_team_class = matched.team_class
            prepared.matched_summary = {
                "trackId": matched.tracklet.track_id,
                "matchScore": matched.match_score,
                "teamClass": str(matched.team_class),
                "isTied": matched.is_tied,
            }
        return matched

    def _select_crops(
        self,
        prepared: _PreparedEvent,
        matched: MatchedTracklet,
        frames: list[ExtractedFrame],
        ctx: _Pass2Context,
    ) -> None:
        started = time.monotonic()
        selected = self._crop_selector.select_and_build_sheet(
            tracklet=matched.tracklet,
            frames=frames,
            max_crops=ctx.request.analysis_options.pass2_max_crops_per_track,
            selection_context=self._crop_selection_context(prepared, matched, ctx),
            observation_report=prepared.crop_report,
        )
        prepared.timings.crop_selection_sec = time.monotonic() - started
        if selected is None:
            prepared.failure_reason = Pass2FailureReason.NO_USABLE_CAP_CROPS
            return
        prepared.selected_crops = selected
        prepared.cap_ambiguous = selected.has_ambiguous_cap
        if ctx.debug_dir is not None:
            self._crop_selector.save_debug_artifacts(
                debug_dir=ctx.debug_dir,
                event_idx=prepared.event_index,
                selected=selected,
                frames=frames,
                obs=matched.nearest_observation,
            )

    @staticmethod
    def _crop_selection_context(
        prepared: _PreparedEvent,
        matched: MatchedTracklet,
        ctx: _Pass2Context,
    ) -> CropSelectionContext:
        """Collect the event evidence used to filter and rank crops of the matched tracklet."""
        analysis = ctx.analysis
        profile = analysis.profile
        tracklets = prepared.tracking.tracklets if prepared.tracking else []
        return CropSelectionContext(
            event_time_sec=prepared.candidate.video_time_sec,
            geometry=profile.cap_zone_geometry,
            expected_color=profile.expected_actor_cap_color(
                prepared.candidate.definition, analysis.tracked_cap_color_hint
            ),
            opponent_color=analysis.hints.opponent_cap_color_hint,
            tracked_color=analysis.tracked_cap_color_hint,
            goalkeeper_color=profile.goalkeeper_cap_color,
            other_tracklets=tuple(t for t in tracklets if t.track_id != matched.tracklet.track_id),
        )

    # -------------------------------------------------------- Pass 2 / Phase B

    def _run_phase_b(
        self,
        prepared: list[_PreparedEvent],
        ctx: _Pass2Context,
        state: _CapBreakerState,
        progress: _ProgressTracker,
    ) -> list[_CapOutcome]:
        """Resolve cap numbers for all prepared events of one batch.

        Operates on the full list of prepared Phase A items so a batched multi-event OCR call
        can replace the per-item ``resolve`` later without touching Phase A. A breaker trip
        never discards Phase A work: affected events keep their crops and get an explicit
        failure reason (or the local OCR fallback result).
        """

        def resolve(item: _PreparedEvent) -> _CapOutcome:
            try:
                outcome = self._resolve_cap(item, ctx, state)
            finally:
                # Release the crop bundle as soon as its outcome is recorded; later stages only
                # need the outcome, so memory does not accumulate across a long match.
                item.selected_crops = None
            progress.advance("pass2_cap_resolution", _PHASE_B_PROGRESS_UNITS)
            return outcome

        return ordered_parallel_map(
            resolve,
            prepared,
            self._settings.pass2_phase_b_max_concurrency,
            before_task=lambda: self._check_cancelled(ctx.cancel_event),
        )

    def _resolve_cap(
        self,
        item: _PreparedEvent,
        ctx: _Pass2Context,
        state: _CapBreakerState,
    ) -> _CapOutcome:
        if item.selected_crops is None:
            return _CapOutcome(failure_reason=item.failure_reason)

        started = time.monotonic()
        try:
            result, used_fallback = self._invoke_cap_resolver(item, ctx, state)
            outcome = _CapOutcome(
                cap_result=result,
                token_usage=result.token_usage,
                used_fallback=used_fallback,
            )
        except GeminiCallBlockedError as exc:
            state.block(exc.reason, exc.detail)
            outcome = _CapOutcome(
                failure_reason=failure_reason_for_breaker(exc.reason),
                blocked_by_breaker=True,
            )
        except Exception as exc:
            logger.warning("Cap resolution failed for event #%d: %s", item.event_index, exc)
            usage = getattr(exc, "token_usage", None)
            outcome = _CapOutcome(
                failure_reason=Pass2FailureReason.CAP_RESOLUTION_API_ERROR,
                token_usage=usage if isinstance(usage, GeminiTokenUsage) else None,
                warnings=[f"Event #{item.event_index} cap resolution failed: {exc}"],
            )
        outcome.elapsed_sec = time.monotonic() - started
        return outcome

    def _invoke_cap_resolver(
        self,
        item: _PreparedEvent,
        ctx: _Pass2Context,
        state: _CapBreakerState,
    ) -> tuple[CapResolutionResult, bool]:
        """Call the primary resolver, or the local fallback once the Gemini breaker is open."""
        if state.reason is None:
            try:
                return self._call_resolver(self._cap_resolver, item, ctx), False
            except GeminiCallBlockedError as exc:
                state.block(exc.reason, exc.detail)
                if self._fallback_cap_resolver is None:
                    raise
        if self._fallback_cap_resolver is None:
            assert state.reason is not None
            raise GeminiCallBlockedError("pass2", state.reason, state.detail)
        with state.fallback_lock:
            return self._call_resolver(self._fallback_cap_resolver, item, ctx), True

    @staticmethod
    def _call_resolver(
        resolver: BaseCapResolver,
        item: _PreparedEvent,
        ctx: _Pass2Context,
    ) -> CapResolutionResult:
        assert item.selected_crops is not None
        profile = ctx.analysis.profile
        expected_color = profile.expected_actor_cap_color(
            item.candidate.definition, ctx.cap_color_hint
        )
        return resolver.resolve(
            selected_crops=item.selected_crops,
            allowed_cap_numbers=ctx.allowed_caps,
            cap_color_hint=expected_color or item.candidate.cap_color_hint,
            crop_subject=profile.pass2_crop_subject,
            occlusion_hint=profile.pass2_occlusion_hint,
            opponent_cap_color=ctx.analysis.hints.opponent_cap_color_hint,
            goalkeeper_cap_color=profile.goalkeeper_cap_color,
        )

    @staticmethod
    def _fallback_summary(outcomes: list[_CapOutcome], events: list[DraftEvent]) -> list[str]:
        """Summarize breaker effects; ``events`` align with ``outcomes`` by index.

        Only events that ended up with a confirmed lineup count as resolved by the local OCR
        fallback: failed OCR attempts and cap-color rejections are not "resolved".
        """
        fallback_count = sum(
            1
            for outcome, event in zip(outcomes, events, strict=True)
            if outcome.used_fallback and event.match_lineup_id is not None
        )
        blocked = [outcome for outcome in outcomes if outcome.blocked_by_breaker]
        messages: list[str] = []
        if fallback_count:
            messages.append(
                f"Gemini Pass 2 stopped; {fallback_count} event(s) resolved "
                "with the local OCR fallback."
            )
        if blocked:
            reasons = ", ".join(sorted({str(outcome.failure_reason) for outcome in blocked}))
            messages.append(
                f"Gemini Pass 2 stopped; {len(blocked)} event(s) left unresolved ({reasons}). "
                "Phase A tracking results were preserved."
            )
        return messages

    # ------------------------------------------------------------ Draft events

    def _finish_event(
        self,
        item: _PreparedEvent,
        outcome: _CapOutcome,
        ctx: _Pass2Context,
        video_start_utc: datetime,
        counters: Pass2Counters,
    ) -> DraftEvent:
        """Resolve the lineup, build the draft event, and record debug data and counters."""
        analysis = ctx.analysis
        cand = item.candidate
        expected_color = analysis.profile.expected_actor_cap_color(
            cand.definition, analysis.tracked_cap_color_hint
        )
        lineup = self._resolve_lineup(item, outcome, ctx, expected_color)
        counters.record_event(
            item.match_result,
            lineup.gate_outcomes.get(REVIEW_OCR_CAP_COLOR_MISMATCH, False),
            MIN_BALL_DETECTIONS,
        )
        if ctx.debug_dir is not None:
            self._write_event_json(item, outcome, lineup, expected_color, ctx)
        return self._build_draft_event(cand, lineup, video_start_utc)

    def _resolve_lineup(
        self,
        item: _PreparedEvent,
        outcome: _CapOutcome,
        ctx: _Pass2Context,
        expected_color: str | None,
    ) -> LineupMatchOutcome:
        cand = item.candidate
        failure = item.failure_reason or outcome.failure_reason
        evidence = (
            ActorEvidence(item.actor_match_score, item.actor_team_class or TeamClass.UNKNOWN)
            if item.actor_match_score is not None
            else None
        )
        lineup = self._lineup_matcher.resolve(
            candidate=cand,
            cap_result=outcome.cap_result,
            context=ctx.analysis,
            min_cap_confidence=ctx.request.analysis_options.min_cap_confidence,
            has_spatial_hint=item.has_spatial_hint,
            actor_match_tied=item.actor_match_tied,
            cap_ambiguous=item.cap_ambiguous,
            pass2_failure_reason=str(failure) if failure is not None else None,
            actor_evidence=evidence,
        )
        cap_result = outcome.cap_result
        return self._apply_cap_color_guard(
            lineup,
            cand,
            expected_color,
            cap_result.observed_cap_color if cap_result else None,
            tile_cap_colors=cap_result.tile_cap_colors if cap_result else (),
            opponent_cap_color=ctx.analysis.hints.opponent_cap_color_hint,
        )

    @staticmethod
    def _write_event_json(
        item: _PreparedEvent,
        outcome: _CapOutcome,
        lineup: LineupMatchOutcome,
        expected_color: str | None,
        ctx: _Pass2Context,
    ) -> None:
        failure = item.failure_reason or outcome.failure_reason
        record = Pass2DebugRecord(
            event_index=item.event_index,
            video_time_sec=item.candidate.video_time_sec,
            event_name=item.candidate.definition.name,
            failure_reason=str(failure) if failure is not None else None,
            match_result=item.match_result,
            matched_summary=item.matched_summary,
            crop_report=item.crop_report,
            cap_result=outcome.cap_result,
            expected_cap_color=expected_color,
            opponent_cap_color=ctx.analysis.hints.opponent_cap_color_hint,
            gate_outcomes=lineup.gate_outcomes,
            review_reasons=lineup.review_reasons,
        )
        if ctx.debug_dir is not None:
            write_event_json(ctx.debug_dir, item.event_index, build_event_payload(record))

    @staticmethod
    def _attribution_summary(counters: Pass2Counters, debug_dir: Path | None) -> list[str]:
        """Log the per-job counters, persist them in the debug dir, and return any warning."""
        logger.info(counters.summary_line())
        if debug_dir is not None:
            write_summary_json(debug_dir, counters)
        return [counters.summary_line()] if counters.needs_warning() else []

    @staticmethod
    def _build_draft_event(
        cand: Pass1EventCandidate,
        lineup: LineupMatchOutcome,
        video_start_utc: datetime,
    ) -> DraftEvent:
        return DraftEvent(
            id=uuid4(),
            event_definition_id=cand.definition.event_definition_id,
            prompt_code=cand.definition.prompt_code,
            event_name=cand.definition.name,
            is_positive=cand.definition.is_positive,
            is_shot=cand.definition.is_shot,
            period_number=cand.period_number,
            video_time_sec=cand.video_time_sec,
            event_timestamp=to_utc_timestamp(video_start_utc, cand.video_time_sec),
            is_lead_to_goal=cand.is_lead_to_goal,
            match_lineup_id=lineup.match_lineup_id,
            resolved_cap_number=lineup.resolved_cap_number,
            candidate_lineup_ids=lineup.candidate_lineup_ids,
            actor_bbox_normalized=cand.actor_bbox_normalized,
            pool_zone_hint=cand.pool_zone_hint,
            event_confidence=round(cand.confidence, 4),
            cap_confidence=lineup.cap_confidence,
            needs_review=lineup.needs_review,
            review_reasons=list(lineup.review_reasons),
        )

    @classmethod
    def _apply_cap_color_guard(
        cls,
        outcome: LineupMatchOutcome,
        candidate: Pass1EventCandidate,
        tracked_team_hint: str | None,
        observed_cap_color: str | None = None,
        *,
        tile_cap_colors: tuple[str | None, ...] = (),
        opponent_cap_color: str | None = None,
    ) -> LineupMatchOutcome:
        """Block auto-confirmation when a cap color contradicts the expected actor.

        ``tracked_team_hint`` is the cap color the acting player is expected to wear: the tracked
        team's color, or the sport's goalkeeper color for goalkeeper-only events. Compared with
        it are the Pass 1 ``cap_color_hint`` and the color the OCR resolver observed on the
        tracklet (``observed_cap_color``). The per-tile colors additionally expose a mixed or
        opponent-colored sheet that the model summarized as no color at all.
        """
        hint_mismatch = cls._has_cap_color_hint_mismatch(
            candidate.cap_color_hint, tracked_team_hint
        )
        observed_mismatch = cls._has_observed_color_mismatch(
            observed_cap_color, tracked_team_hint
        ) or cls._tiles_show_opponent_color(observed_cap_color, tile_cap_colors, opponent_cap_color)
        if not (hint_mismatch or observed_mismatch):
            return outcome
        reasons = list(outcome.review_reasons)
        gates = dict(outcome.gate_outcomes)
        if hint_mismatch:
            _append_reason(reasons, _CAP_COLOR_HINT_MISMATCH_REASON)
        if observed_mismatch:
            _append_reason(reasons, REVIEW_OCR_CAP_COLOR_MISMATCH)
            gates[REVIEW_OCR_CAP_COLOR_MISMATCH] = True
        return LineupMatchOutcome(
            match_lineup_id=None,
            resolved_cap_number=None,
            cap_confidence=outcome.cap_confidence,
            candidate_lineup_ids=outcome.candidate_lineup_ids,
            needs_review=True,
            review_reasons=reasons,
            gate_outcomes=gates,
        )

    @staticmethod
    def _has_observed_color_mismatch(observed: str | None, expected: str | None) -> bool:
        """Return True when OCR saw a cap color that differs from the expected one."""
        if not normalize_color_name(observed) or not normalize_color_name(expected):
            return False
        return not colors_match(observed, expected)

    @staticmethod
    def _tiles_show_opponent_color(
        observed: str | None,
        tile_cap_colors: tuple[str | None, ...],
        opponent: str | None,
    ) -> bool:
        """Return True when per-tile OCR colors point at the opponent's cap.

        Fires when at least half of the reported tile colors match the opponent color, or when
        the model reported no overall color and any tile shows the opponent color. A clean sheet
        (overall color reported) with only a small minority of noisy tiles does not fire.
        """
        reported = [color for color in tile_cap_colors if normalize_color_name(color)]
        if not reported or not normalize_color_name(opponent):
            return False
        opponent_tiles = sum(colors_match(color, opponent) for color in reported)
        if not normalize_color_name(observed) and opponent_tiles > 0:
            return True
        return opponent_tiles * 2 >= len(reported)

    @staticmethod
    def _normalize_cap_color_hint(value: str | None) -> str:
        return " ".join(re.sub(r"[-_]+", " ", (value or "").strip().lower()).split())

    @classmethod
    def _has_cap_color_hint_mismatch(
        cls,
        candidate_hint: str | None,
        tracked_team_hint: str | None,
    ) -> bool:
        norm_candidate = cls._normalize_cap_color_hint(candidate_hint)
        norm_tracked = cls._normalize_cap_color_hint(tracked_team_hint)
        return bool(norm_candidate and norm_tracked and norm_candidate != norm_tracked)

    @staticmethod
    def _check_cancelled(cancel_event: threading.Event | None) -> None:
        if cancel_event is not None and cancel_event.is_set():
            raise JobCancelledError("Analysis job was cancelled.")

    @staticmethod
    def _report(
        callback: Callable[[str, int], None] | None,
        stage: str,
        progress: int,
    ) -> None:
        if callback is not None:
            callback(stage, progress)


def _append_reason(reasons: list[str], reason: str) -> None:
    """Append ``reason`` unless already present, preserving order."""
    if reason not in reasons:
        reasons.append(reason)
