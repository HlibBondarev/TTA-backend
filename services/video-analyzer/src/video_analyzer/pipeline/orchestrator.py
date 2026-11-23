"""End-to-end two-pass Water Polo video analysis orchestrator."""

from __future__ import annotations

import logging
import tempfile
import threading
import time
from collections.abc import Callable
from pathlib import Path
from uuid import uuid4

from video_analyzer.config import Settings
from video_analyzer.detectors.base import BaseObjectDetector
from video_analyzer.media.ffmpeg_client import FFmpegClient, VideoChunk, VideoProbeResult
from video_analyzer.pass1.anchor_reconciler import AnchorReconciler
from video_analyzer.pass1.deduplicator import EventDeduplicator
from video_analyzer.pass1.gemini_client import BasePass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1EventCandidate
from video_analyzer.pass2.actor_matcher import ActorMatcher
from video_analyzer.pass2.cap_resolver import BaseCapResolver, CapResolutionResult
from video_analyzer.pass2.crop_selector import CropSelector
from video_analyzer.pass2.lineup_matcher import LineupMatcher
from video_analyzer.pass2.tracker import BoTSortTracker
from video_analyzer.pipeline.timestamp_aligner import (
    resolve_video_start_utc,
    synthesize_closed_presences,
    to_utc_timestamp,
)
from video_analyzer.schemas.draft import (
    DraftEvent,
    DraftTimeAnchor,
    ModelMetadata,
    PipelineDiagnostics,
    VideoAnalysisDraft,
    VideoMetadata,
)
from video_analyzer.schemas.request import AnalyzeJobRequest

logger = logging.getLogger(__name__)


class JobCancelledError(RuntimeError):
    """Raised when an in-flight video analysis job is cancelled cooperatively."""


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
    ) -> None:
        self._settings = settings
        self._ffmpeg = ffmpeg_client
        self._pass1 = pass1_analyzer
        self._detector = detector
        self._cap_resolver = cap_resolver
        self._deduplicator = deduplicator or EventDeduplicator()
        self._anchor_reconciler = anchor_reconciler or AnchorReconciler()
        self._tracker = tracker or BoTSortTracker()
        self._actor_matcher = actor_matcher or ActorMatcher()
        self._crop_selector = crop_selector or CropSelector()
        self._lineup_matcher = lineup_matcher or LineupMatcher()

    def run(
        self,
        request: AnalyzeJobRequest,
        cancel_event: threading.Event | None = None,
        progress_callback: Callable[[str, int], None] | None = None,
        debug_dir: Path | None = None,
    ) -> VideoAnalysisDraft:
        """Execute the full two-pass analysis and return a TTA-compatible draft."""
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
            video_path = self._ffmpeg.resolve_source_path(request.video_source, work_dir)
            probe = self._ffmpeg.probe(video_path)
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

            anchor_cands, anchor_warnings = self._anchor_reconciler.reconcile(
                chunk_results=chunk_results,
                total_duration_sec=probe.duration_sec,
            )
            warnings.extend(anchor_warnings)

            draft_anchors = [
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

            draft_events, pass2_windows, resolved_count, unresolved_count, pass2_timings = (
                self._run_pass2(
                    video_path=video_path,
                    probe=probe,
                    request=request,
                    candidates=events_cand,
                    video_start_utc=video_start_utc,
                    work_dir=work_dir,
                    cancel_event=cancel_event,
                    progress_callback=progress_callback,
                    debug_dir=debug_dir,
                    warnings=warnings,
                )
            )
            timings.update(pass2_timings)

            presences = synthesize_closed_presences(
                events=draft_events,
                anchors=draft_anchors,
                video_start_utc=video_start_utc,
                enabled=request.analysis_options.generate_player_presences,
            )

            timings["total"] = round(time.monotonic() - t_start, 3)
            detector_info = self._detector.provider_info()

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
                model_metadata=ModelMetadata(
                    pass1_vlm_model=self._settings.gemini_pass1_model,
                    detector_provider=detector_info.get(
                        "provider", self._settings.detector_provider
                    ),
                    detector_model=detector_info.get(
                        "model", self._settings.detector_model_path_or_id
                    ),
                    tracker=self._settings.tracker_type,
                    cap_resolver=(
                        "skipped"
                        if request.analysis_options.skip_pass2
                        else self._settings.cap_resolver_provider
                    ),
                ),
                time_anchors=draft_anchors,
                events=draft_events,
                player_presences=presences,
                diagnostics=PipelineDiagnostics(
                    pass1_chunks_processed=len(chunk_results),
                    pass1_raw_candidate_count=raw_count,
                    deduplicated_event_count=len(events_cand),
                    pass2_windows_processed=pass2_windows,
                    resolved_actor_count=resolved_count,
                    unresolved_actor_count=unresolved_count,
                    warnings=warnings,
                    timings_sec=timings,
                ),
            )

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
        t_chunk = time.monotonic()
        chunks = self._ffmpeg.create_pass1_chunks(
            video_path=video_path,
            probe=probe,
            output_dir=work_dir / "pass1_chunks",
            chunk_duration_sec=opts.pass1_chunk_duration_sec,
            overlap_sec=opts.pass1_overlap_sec,
            target_height=opts.pass1_target_height,
            fps=opts.pass1_fps,
        )
        chunk_sec = round(time.monotonic() - t_chunk, 3)

        t_vlm = time.monotonic()
        chunk_results: list[tuple[VideoChunk, Pass1ChunkResult]] = []
        total_chunks = max(1, len(chunks))
        for idx, chunk in enumerate(chunks):
            self._check_cancelled(cancel_event)
            pct = 15 + int(((idx + 1) / total_chunks) * 35)
            self._report(progress_callback, "pass1_vlm", pct)
            result = self._pass1.analyze_chunk(request.match_snapshot, chunk)
            chunk_results.append((chunk, result))

        vlm_sec = round(time.monotonic() - t_vlm, 3)
        return chunk_results, {
            "pass1TranscodeAndChunk": chunk_sec,
            "pass1Vlm": vlm_sec,
        }

    def _run_pass2(
        self,
        video_path: Path,
        probe: VideoProbeResult,
        request: AnalyzeJobRequest,
        candidates: list[Pass1EventCandidate],
        video_start_utc,
        work_dir: Path,
        cancel_event: threading.Event | None,
        progress_callback: Callable[[str, int], None] | None,
        debug_dir: Path | None,
        warnings: list[str],
    ) -> tuple[list[DraftEvent], int, int, int, dict[str, float]]:
        opts = request.analysis_options
        allowed_caps = sorted(
            {
                entry.number
                for entry in request.match_snapshot.lineup
                if entry.team_id == request.tracked_team_id
            }
        )
        cap_color_hint = request.match_snapshot.tracked_team.cap_color_hint

        draft_events: list[DraftEvent] = []
        windows_processed = 0
        resolved_count = 0
        unresolved_count = 0
        track_elapsed = 0.0
        cap_elapsed = 0.0

        total_events = max(1, len(candidates))
        for idx, cand in enumerate(candidates):
            self._check_cancelled(cancel_event)
            pct = 55 + int(((idx + 1) / total_events) * 40)
            self._report(progress_callback, "pass2_tracking", pct)

            cap_result: CapResolutionResult | None = None
            if not opts.skip_pass2:
                windows_processed += 1
                cap_result, t_trk, t_cap = self._inspect_event_window(
                    event_index=idx + 1,
                    candidate=cand,
                    video_path=video_path,
                    probe=probe,
                    request=request,
                    allowed_caps=allowed_caps,
                    cap_color_hint=cap_color_hint,
                    work_dir=work_dir,
                    debug_dir=debug_dir,
                    warnings=warnings,
                )
                track_elapsed += t_trk
                cap_elapsed += t_cap

            outcome = self._lineup_matcher.resolve(
                candidate=cand,
                cap_result=cap_result,
                snapshot=request.match_snapshot,
                min_cap_confidence=opts.min_cap_confidence,
            )
            if outcome.match_lineup_id is not None:
                resolved_count += 1
            else:
                unresolved_count += 1

            draft_events.append(
                DraftEvent(
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
                    match_lineup_id=outcome.match_lineup_id,
                    resolved_cap_number=outcome.resolved_cap_number,
                    candidate_lineup_ids=outcome.candidate_lineup_ids,
                    actor_bbox_normalized=cand.actor_bbox_normalized,
                    pool_zone_hint=cand.pool_zone_hint,
                    event_confidence=round(cand.confidence, 4),
                    cap_confidence=outcome.cap_confidence,
                    needs_review=outcome.needs_review,
                    review_reasons=outcome.review_reasons,
                )
            )

        return (
            draft_events,
            windows_processed,
            resolved_count,
            unresolved_count,
            {
                "pass2ExtractAndTrack": round(track_elapsed, 3),
                "pass2CapResolution": round(cap_elapsed, 3),
            },
        )

    def _inspect_event_window(
        self,
        event_index: int,
        candidate: Pass1EventCandidate,
        video_path: Path,
        probe: VideoProbeResult,
        request: AnalyzeJobRequest,
        allowed_caps: list[int],
        cap_color_hint: str | None,
        work_dir: Path,
        debug_dir: Path | None,
        warnings: list[str],
    ) -> tuple[CapResolutionResult | None, float, float]:
        opts = request.analysis_options
        t_trk_start = time.monotonic()
        win_dir = work_dir / f"pass2_win_{event_index:04d}"
        frames = self._ffmpeg.extract_window_frames(
            video_path=video_path,
            center_sec=candidate.video_time_sec,
            half_window_sec=opts.pass2_window_half_sec,
            total_duration_sec=probe.duration_sec,
            sample_fps=opts.pass2_sample_fps,
            work_dir=win_dir,
        )
        if not frames:
            warnings.append(
                f"Event #{event_index} ({candidate.definition.short_name} at "
                f"{candidate.video_time_sec}s): no frames extracted in Pass 2 window."
            )
            return None, round(time.monotonic() - t_trk_start, 3), 0.0

        tracklets = self._tracker.track_window(frames, self._detector)
        frame_h, frame_w = frames[0].image_bgr.shape[:2]
        matched = self._actor_matcher.match(
            candidate=candidate,
            tracklets=tracklets,
            snapshot=request.match_snapshot,
            frame_width=frame_w,
            frame_height=frame_h,
        )
        if matched is None:
            return None, round(time.monotonic() - t_trk_start, 3), 0.0

        selected_crops = self._crop_selector.select_and_build_sheet(
            tracklet=matched.tracklet,
            frames=frames,
            max_crops=opts.pass2_max_crops_per_track,
        )
        t_trk_elapsed = time.monotonic() - t_trk_start

        if selected_crops is None:
            return None, t_trk_elapsed, 0.0

        if debug_dir is not None:
            self._crop_selector.save_debug_artifacts(
                debug_dir=debug_dir,
                event_idx=event_index,
                selected=selected_crops,
                frames=frames,
                obs=matched.nearest_observation,
            )

        t_cap_start = time.monotonic()
        try:
            cap_result = self._cap_resolver.resolve(
                selected_crops=selected_crops,
                allowed_cap_numbers=allowed_caps,
                cap_color_hint=candidate.cap_color_hint or cap_color_hint,
            )
        except Exception as exc:
            logger.warning("Cap resolution failed for event #%d: %s", event_index, exc)
            warnings.append(f"Event #{event_index} cap resolution failed: {exc}")
            cap_result = None
        t_cap_elapsed = time.monotonic() - t_cap_start

        return cap_result, t_trk_elapsed, t_cap_elapsed

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
