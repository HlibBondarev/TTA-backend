"""End-to-end two-pass Water Polo video analysis orchestrator."""

from __future__ import annotations

import logging
import re
import tempfile
import threading
import time
from collections.abc import Callable
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
from video_analyzer.pass1.deduplicator import EventDeduplicator
from video_analyzer.pass1.gemini_client import BasePass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1EventCandidate
from video_analyzer.pass2.actor_matcher import ActorMatcher, MatchedTracklet
from video_analyzer.pass2.cap_resolver import BaseCapResolver, CapResolutionResult
from video_analyzer.pass2.crop_selector import CropSelector, SelectedCrops
from video_analyzer.pass2.failure_reasons import Pass2FailureReason, failure_reason_for_breaker
from video_analyzer.pass2.lineup_matcher import LineupMatcher, LineupMatchOutcome
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
from video_analyzer.usage import build_gemini_usage_diagnostics

logger = logging.getLogger(__name__)
_CAP_COLOR_HINT_MISMATCH_REASON = "CAP_COLOR_HINT_MISMATCH"
_GEMINI_CAP_RESOLVER_PROVIDER = "gemini_crop_grid"

_PASS1_PROGRESS_BASE = 15
_PASS1_PROGRESS_SPAN = 35
_PHASE_A_PROGRESS_BASE = 55
_PHASE_A_PROGRESS_SPAN = 30
_PHASE_B_PROGRESS_BASE = 85
_PHASE_B_PROGRESS_SPAN = 10


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
    # Per-frame ball/goal detections are kept for later actor-attribution work (#159).
    tracking: TrackingResult | None = None


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
    """Thread-safe progress reporter advancing one step per completed task."""

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

    def advance(self) -> None:
        if self._callback is None:
            return
        with self._lock:
            self._done += 1
            pct = self._base + int((self._done / self._total) * self._span)
        self._callback(self._stage, pct)


class VideoAnalysisOrchestrator:
    """Coordinates Pass 1 (VLM) and Pass 2 (4K tracking + cap resolution)."""

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
    ) -> None:
        self._settings = settings
        self._ffmpeg = ffmpeg_client
        self._pass1 = pass1_analyzer
        self._detector = self._limit_detector_concurrency(detector, settings)
        self._cap_resolver = cap_resolver
        self._fallback_cap_resolver = fallback_cap_resolver
        self._deduplicator = deduplicator or EventDeduplicator()
        self._anchor_reconciler = anchor_reconciler or AnchorReconciler()
        self._tracker = tracker or BoTSortTracker()
        self._actor_matcher = actor_matcher or ActorMatcher()
        self._crop_selector = crop_selector or CropSelector()
        self._lineup_matcher = lineup_matcher or LineupMatcher()

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

            chunk_results, pass1_timings = self._run_pass1(
                video_path=video_path,
                probe=probe,
                request=request,
                work_dir=work_dir,
                cancel_event=cancel_event,
                progress_callback=progress_callback,
            )
            timings.update(pass1_timings)

            events_cand, raw_count, dedup_warnings = self._deduplicator.process(
                chunk_results=chunk_results,
                event_definitions=request.match_snapshot.event_definitions,
                total_duration_sec=probe.duration_sec,
                min_confidence=request.analysis_options.min_event_confidence,
            )
            warnings.extend(dedup_warnings)

            draft_anchors, anchor_warnings = self._build_anchors(
                chunk_results, probe, video_start_utc
            )
            warnings.extend(anchor_warnings)

            pass2 = self._run_pass2(
                video_path=video_path,
                probe=probe,
                request=request,
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
                    pass1_raw_candidate_count=raw_count,
                    deduplicated_event_count=len(events_cand),
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

    def _build_anchors(
        self,
        chunk_results: list[tuple[VideoChunk, Pass1ChunkResult]],
        probe: VideoProbeResult,
        video_start_utc: datetime,
    ) -> tuple[list[DraftTimeAnchor], list[str]]:
        anchor_cands, anchor_warnings = self._anchor_reconciler.reconcile(
            chunk_results=chunk_results,
            total_duration_sec=probe.duration_sec,
        )
        anchors = [
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
        return anchors, anchor_warnings

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
        work_dir: Path,
        cancel_event: threading.Event | None,
        progress_callback: Callable[[str, int], None] | None,
    ) -> tuple[list[tuple[VideoChunk, Pass1ChunkResult]], dict[str, float]]:
        opts = request.analysis_options
        self._report(progress_callback, "pass1_chunking", 10)
        chunk_timings: dict[str, float] = {}
        t_chunk = time.monotonic()
        chunks = self._ffmpeg.create_pass1_chunks(
            video_path=video_path,
            probe=probe,
            output_dir=work_dir / "pass1_chunks",
            chunk_duration_sec=opts.pass1_chunk_duration_sec,
            overlap_sec=opts.pass1_overlap_sec,
            target_height=opts.pass1_target_height,
            fps=opts.pass1_fps,
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
            result = self._pass1.analyze_chunk(request.match_snapshot, chunk)
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
        return chunk_results, {
            "pass1TranscodeAndChunk": chunk_sec,
            "pass1Mezzanine": chunk_timings.get("pass1Mezzanine", 0.0),
            "pass1ChunkSlicing": chunk_timings.get("pass1ChunkSlicing", 0.0),
            "pass1Vlm": vlm_sec,
        }

    # ------------------------------------------------------------------ Pass 2

    def _run_pass2(
        self,
        video_path: Path,
        probe: VideoProbeResult,
        request: AnalyzeJobRequest,
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
            allowed_caps=sorted(
                {
                    entry.number
                    for entry in request.match_snapshot.lineup
                    if entry.team_id == request.tracked_team_id
                }
            ),
            cap_color_hint=request.match_snapshot.tracked_team.cap_color_hint,
            cancel_event=cancel_event,
            debug_dir=debug_dir,
        )
        result = _Pass2Result()
        if request.analysis_options.skip_pass2:
            prepared = [self._skipped_event(i, cand) for i, cand in enumerate(candidates, 1)]
            outcomes = [_CapOutcome() for _ in prepared]
        else:
            prepared = self._run_phase_a(candidates, ctx, progress_callback)
            outcomes = self._run_phase_b(prepared, ctx, progress_callback)
            result.windows_processed = len(prepared)

        for item, outcome in zip(prepared, outcomes, strict=True):
            warnings.extend(item.warnings)
            warnings.extend(outcome.warnings)
            if outcome.token_usage is not None:
                result.usages.append(outcome.token_usage)
            result.events.append(self._build_draft_event(item, outcome, request, video_start_utc))
        warnings.extend(self._fallback_summary(outcomes))
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

    # -------------------------------------------------------- Pass 2 / Phase A

    def _run_phase_a(
        self,
        candidates: list[Pass1EventCandidate],
        ctx: _Pass2Context,
        progress_callback: Callable[[str, int], None] | None,
    ) -> list[_PreparedEvent]:
        """Extract frames, detect, track and select crops for every event."""
        progress = _ProgressTracker(
            progress_callback,
            "pass2_tracking",
            len(candidates),
            _PHASE_A_PROGRESS_BASE,
            _PHASE_A_PROGRESS_SPAN,
        )

        def prepare(indexed: tuple[int, Pass1EventCandidate]) -> _PreparedEvent:
            prepared = self._prepare_event(indexed[0], indexed[1], ctx)
            progress.advance()
            return prepared

        return ordered_parallel_map(
            prepare,
            list(enumerate(candidates, start=1)),
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
            prepared.failure_reason = Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED
            return prepared

        self._select_crops(prepared, matched, frames, ctx)
        return prepared

    def _extract_frames(self, prepared: _PreparedEvent, ctx: _Pass2Context) -> list[ExtractedFrame]:
        opts = ctx.request.analysis_options
        started = time.monotonic()
        frames = self._ffmpeg.extract_window_frames(
            video_path=ctx.video_path,
            center_sec=prepared.candidate.video_time_sec,
            half_window_sec=opts.pass2_window_half_sec,
            total_duration_sec=ctx.probe.duration_sec,
            sample_fps=opts.pass2_sample_fps,
            frame_width=ctx.probe.width,
            frame_height=ctx.probe.height,
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
        tracking = self._tracker.track_window_with_scene(frames, timed_detector)
        elapsed = time.monotonic() - started
        prepared.tracking = tracking
        prepared.timings.detection_sec = timed_detector.elapsed_sec
        prepared.timings.tracking_sec = max(0.0, elapsed - timed_detector.elapsed_sec)

        matched = self._actor_matcher.match(
            candidate=prepared.candidate,
            tracklets=tracking.tracklets,
            snapshot=ctx.request.match_snapshot,
            frame_width=frames[0].native_width,
            frame_height=frames[0].native_height,
        )
        if matched is not None:
            prepared.has_spatial_hint = matched.has_spatial_hint
            prepared.actor_match_tied = matched.is_tied
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

    # -------------------------------------------------------- Pass 2 / Phase B

    def _run_phase_b(
        self,
        prepared: list[_PreparedEvent],
        ctx: _Pass2Context,
        progress_callback: Callable[[str, int], None] | None,
    ) -> list[_CapOutcome]:
        """Resolve cap numbers for all prepared events.

        Operates on the full list of prepared Phase A items so a batched multi-event OCR call
        can replace the per-item ``resolve`` later without touching Phase A. A breaker trip
        never discards Phase A work: affected events keep their crops and get an explicit
        failure reason (or the local OCR fallback result).
        """
        state = _CapBreakerState()
        progress = _ProgressTracker(
            progress_callback,
            "pass2_cap_resolution",
            len(prepared),
            _PHASE_B_PROGRESS_BASE,
            _PHASE_B_PROGRESS_SPAN,
        )

        def resolve(item: _PreparedEvent) -> _CapOutcome:
            outcome = self._resolve_cap(item, ctx, state)
            progress.advance()
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
        return resolver.resolve(
            selected_crops=item.selected_crops,
            allowed_cap_numbers=ctx.allowed_caps,
            cap_color_hint=ctx.cap_color_hint or item.candidate.cap_color_hint,
        )

    @staticmethod
    def _fallback_summary(outcomes: list[_CapOutcome]) -> list[str]:
        fallback_count = sum(1 for outcome in outcomes if outcome.used_fallback)
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

    def _build_draft_event(
        self,
        item: _PreparedEvent,
        outcome: _CapOutcome,
        request: AnalyzeJobRequest,
        video_start_utc: datetime,
    ) -> DraftEvent:
        cand = item.candidate
        failure = item.failure_reason or outcome.failure_reason
        lineup = self._lineup_matcher.resolve(
            candidate=cand,
            cap_result=outcome.cap_result,
            snapshot=request.match_snapshot,
            min_cap_confidence=request.analysis_options.min_cap_confidence,
            has_spatial_hint=item.has_spatial_hint,
            actor_match_tied=item.actor_match_tied,
            cap_ambiguous=item.cap_ambiguous,
            pass2_failure_reason=str(failure) if failure is not None else None,
        )
        lineup = self._apply_cap_color_guard(
            lineup, cand, request.match_snapshot.tracked_team.cap_color_hint
        )
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
    ) -> LineupMatchOutcome:
        """Block auto-confirmation when the event's cap color contradicts the tracked team."""
        if not cls._has_cap_color_hint_mismatch(candidate.cap_color_hint, tracked_team_hint):
            return outcome
        reasons = list(outcome.review_reasons)
        if _CAP_COLOR_HINT_MISMATCH_REASON not in reasons:
            reasons.append(_CAP_COLOR_HINT_MISMATCH_REASON)
        return LineupMatchOutcome(
            match_lineup_id=None,
            resolved_cap_number=None,
            cap_confidence=outcome.cap_confidence,
            candidate_lineup_ids=outcome.candidate_lineup_ids,
            needs_review=True,
            review_reasons=reasons,
        )

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
