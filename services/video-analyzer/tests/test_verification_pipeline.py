"""Pipeline-level tests: normalize -> dedup -> anchors -> verify -> dead-time -> Pass 2."""

from __future__ import annotations

import threading
from pathlib import Path
from typing import Any

import pytest

from tests.support import CODE_GOAL, CODE_GOAL_CONCEDED, CODE_STEAL
from tests.test_orchestrator_and_api import (
    MockCapResolver,
    MockFFmpegClient,
    MockPass1Analyzer,
    _orchestrator_with,
)
from video_analyzer.config import Settings
from video_analyzer.media.ffmpeg_client import FFmpegCancelledError, VideoChunk
from video_analyzer.pass1.gemini_client import BasePass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawAnchor, Pass1RawEvent
from video_analyzer.pass2.cap_resolver import BaseCapResolver, CapResolutionResult
from video_analyzer.pipeline.factory import build_verifier
from video_analyzer.pipeline.orchestrator import JobCancelledError, VideoAnalysisOrchestrator
from video_analyzer.schemas.draft import GeminiTokenUsage, TimeAnchorType
from video_analyzer.schemas.request import AnalyzeJobRequest
from video_analyzer.sport import AnalysisContext
from video_analyzer.verifier import BaseEventVerifier, GeminiClipVerifier, VerificationResult
from video_analyzer.verifier.apply import mark_event_unverified


class _RecordingVerifier(BaseEventVerifier):
    """Returns its inputs (optionally edited by ``edit``) and records what it received."""

    def __init__(self, edit=None, usages=None, warnings=None) -> None:
        self.calls: list[dict[str, Any]] = []
        self._edit = edit
        self._usages = usages or []
        self._warnings = warnings or []

    def verify(
        self,
        context,
        events,
        anchors,
        mezzanine_path,
        work_dir,
        conflict_groups=(),
        cancel_event=None,
    ) -> VerificationResult:
        self.calls.append(
            {
                "events": list(events),
                "anchors": list(anchors),
                "mezzanine_exists": Path(mezzanine_path).exists(),
                "mezzanine_name": Path(mezzanine_path).name,
                "conflict_groups": list(conflict_groups),
            }
        )
        result = VerificationResult(
            events=list(events),
            anchors=list(anchors),
            usages=list(self._usages),
            warnings=list(self._warnings),
        )
        if self._edit is not None:
            self._edit(result)
        return result


class _SingleChunkPass1(BasePass1Analyzer):
    """Returns a fixed result for the (single) chunk and records every call."""

    def __init__(self, result: Pass1ChunkResult) -> None:
        self._result = result
        self.chunks: list[VideoChunk] = []

    def analyze_chunk(self, context: AnalysisContext, chunk: VideoChunk) -> Pass1ChunkResult:
        self.chunks.append(chunk)
        return self._result


def _raw_event(code: str, time_sec: float, **kwargs: Any) -> Pass1RawEvent:
    return Pass1RawEvent(
        time_sec=time_sec, prompt_code=code, period_number=1, confidence=0.9, **kwargs
    )


def _orchestrator(tmp_path: Path, verifier=None, **kwargs: Any) -> VideoAnalysisOrchestrator:
    orchestrator = _orchestrator_with(
        tmp_path, pass1=kwargs.pop("pass1", MockPass1Analyzer()), **kwargs
    )
    orchestrator._verifier = verifier  # noqa: SLF001 - wire the stage without a factory
    return orchestrator


def test_verifier_receives_deduplicated_candidates_anchors_and_the_mezzanine(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    verifier = _RecordingVerifier()

    draft = _orchestrator(tmp_path, verifier, cap_resolver=MockCapResolver()).run(
        sample_job_request
    )

    assert len(verifier.calls) == 1
    call = verifier.calls[0]
    assert [e.definition.prompt_code for e in call["events"]] == [CODE_GOAL, CODE_GOAL_CONCEDED]
    assert [a.type for a in call["anchors"]] == [
        TimeAnchorType.PERIOD_START,
        TimeAnchorType.PERIOD_END,
    ]
    assert call["mezzanine_exists"] is True
    assert call["mezzanine_name"] == "pass1_mezzanine.mp4"
    assert len(draft.events) == 2
    assert "pass1Verification" in draft.diagnostics.timings_sec


def test_verified_events_feed_dead_time_filtering_before_pass2(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    pass1 = _SingleChunkPass1(
        Pass1ChunkResult(
            time_anchors=[
                Pass1RawAnchor(
                    time_sec=5.0,
                    period_number=1,
                    type=TimeAnchorType.PERIOD_START,
                    confidence=0.95,
                ),
                Pass1RawAnchor(
                    time_sec=100.0,
                    period_number=1,
                    type=TimeAnchorType.STOPPAGE_START,
                    confidence=0.95,
                ),
                Pass1RawAnchor(
                    time_sec=130.0,
                    period_number=1,
                    type=TimeAnchorType.STOPPAGE_END,
                    confidence=0.95,
                ),
            ],
            period_events=[
                _raw_event(CODE_STEAL, 50.0),
                _raw_event(CODE_STEAL, 115.0),  # dead time
                _raw_event(CODE_STEAL, 160.0),
            ],
        )
    )
    verifier = _RecordingVerifier()
    seen_times: list[float] = []

    class _Resolver(BaseCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None, **_hints):
            return CapResolutionResult(
                resolved_cap_number=7, confidence=0.95, candidate_scores={7: 0.95}
            )

    draft = _orchestrator(
        tmp_path,
        verifier,
        pass1=pass1,
        cap_resolver=_Resolver(),
        pass2_phase_a_max_concurrency=1,
        pass2_phase_b_max_concurrency=1,
    ).run(sample_job_request)

    seen_times.extend(event.video_time_sec for event in draft.events)
    assert len(verifier.calls[0]["events"]) == 3  # the verifier still sees every candidate
    assert seen_times == [50.0, 160.0]
    assert any("Dropped dead-time event" in w for w in draft.diagnostics.warnings)
    assert draft.diagnostics.pass2_windows_processed == 2


def test_verifier_usage_and_warnings_reach_the_draft_diagnostics(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    usage = GeminiTokenUsage(
        call_count=1, prompt_tokens=1000, candidates_tokens=50, total_tokens=1050
    )
    verifier = _RecordingVerifier(usages=[usage, usage], warnings=["Verifier note."])

    draft = _orchestrator(
        tmp_path,
        verifier,
        cap_resolver=MockCapResolver(),
        gemini_verifier_model="gemini-4-hydrogen",
    ).run(sample_job_request)

    verifier_usage = draft.diagnostics.gemini_usage.verifier.usage
    assert verifier_usage.call_count == 2
    assert verifier_usage.prompt_tokens == 2000
    assert draft.diagnostics.gemini_usage.total.call_count == 2
    assert "Verifier note." in draft.diagnostics.warnings


def test_unverified_candidates_are_flagged_for_review_in_the_draft(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    def edit(result: VerificationResult) -> None:
        mark_event_unverified(result.events[0])

    draft = _orchestrator(
        tmp_path, _RecordingVerifier(edit=edit), cap_resolver=MockCapResolver()
    ).run(sample_job_request)

    assert draft.events[0].needs_review is True
    assert "UNVERIFIED" in draft.events[0].review_reasons


def test_a_crashing_verifier_fails_open_and_marks_everything_unverified(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    class _CrashingVerifier(BaseEventVerifier):
        def verify(self, *args, **kwargs):
            raise RuntimeError("verifier exploded")

    draft = _orchestrator(tmp_path, _CrashingVerifier(), cap_resolver=MockCapResolver()).run(
        sample_job_request
    )

    assert len(draft.events) == 2
    assert all("UNVERIFIED" in e.review_reasons for e in draft.events)
    assert all(a.needs_review for a in draft.time_anchors)
    assert any("verification failed open" in w for w in draft.diagnostics.warnings)


def test_verifier_cancellation_surfaces_as_a_cancelled_job(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    class _CancellingVerifier(BaseEventVerifier):
        def verify(self, *args, **kwargs):
            raise FFmpegCancelledError("cancelled")

    orchestrator = _orchestrator(tmp_path, _CancellingVerifier())

    with pytest.raises(JobCancelledError):
        orchestrator.run(sample_job_request)


def test_cancel_event_set_before_verification_stops_the_job(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    cancel = threading.Event()
    verifier = _RecordingVerifier()
    orchestrator = _orchestrator(tmp_path, verifier)

    class _CancelAfterPass1(MockPass1Analyzer):
        def analyze_chunk(self, context, chunk):
            cancel.set()
            return super().analyze_chunk(context, chunk)

    orchestrator._pass1 = _CancelAfterPass1()  # noqa: SLF001

    with pytest.raises(JobCancelledError):
        orchestrator.run(sample_job_request, cancel_event=cancel)
    assert verifier.calls == []


def test_without_a_verifier_the_stage_is_skipped(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft = _orchestrator(tmp_path, None, cap_resolver=MockCapResolver()).run(sample_job_request)

    assert draft.diagnostics.timings_sec["pass1Verification"] == 0.0
    assert draft.diagnostics.gemini_usage.verifier.usage.call_count == 0
    assert all("UNVERIFIED" not in e.review_reasons for e in draft.events)


def test_global_timeline_offsets_are_normalized_before_dedup_and_anchors(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    class _TwoChunkFFmpeg(MockFFmpegClient):
        def create_pass1_chunks(self, video_path, probe, output_dir, **kwargs):
            output_dir.mkdir(parents=True, exist_ok=True)
            path = output_dir / "chunk_1.mp4"
            path.write_bytes(b"mp4")
            return [VideoChunk(index=1, start_sec=120.0, end_sec=240.0, path=path)]

    # The model ignored the relative-time rule and returned global times.
    pass1 = _SingleChunkPass1(
        Pass1ChunkResult(
            time_anchors=[
                Pass1RawAnchor(
                    time_sec=130.0,
                    period_number=1,
                    type=TimeAnchorType.PERIOD_START,
                    confidence=0.95,
                )
            ],
            period_events=[_raw_event(CODE_STEAL, 190.0), _raw_event(CODE_STEAL, 235.0)],
        )
    )
    verifier = _RecordingVerifier()

    draft = _orchestrator(
        tmp_path, verifier, pass1=pass1, ffmpeg=_TwoChunkFFmpeg(), cap_resolver=MockCapResolver()
    ).run(sample_job_request)

    assert [e.video_time_sec for e in verifier.calls[0]["events"]] == [190.0, 235.0]
    assert [a.video_time_sec for a in verifier.calls[0]["anchors"]] == [130.0]
    assert any("normalized 3 Pass 1 offset(s)" in w for w in draft.diagnostics.warnings)


def test_goalkeeper_event_uses_the_goalkeeper_cap_color_for_hints_and_guard(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    pass1 = _SingleChunkPass1(
        Pass1ChunkResult(
            period_events=[
                _raw_event(
                    CODE_GOAL_CONCEDED,
                    60.0,
                    cap_color_hint="red",
                    actor_bbox_normalized=[0.45, 0.35, 0.55, 0.65],
                )
            ]
        )
    )
    captured: dict[str, Any] = {}

    class _Resolver(BaseCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None, **hints):
            captured["cap_color_hint"] = cap_color_hint
            captured.update(hints)
            return CapResolutionResult(
                resolved_cap_number=1, confidence=0.95, candidate_scores={1: 0.95}
            )

    draft = _orchestrator(tmp_path, None, pass1=pass1, cap_resolver=_Resolver()).run(
        sample_job_request
    )

    # The tracked team wears white, but its goalkeeper wears red: no CAP_COLOR_HINT_MISMATCH.
    assert captured["cap_color_hint"] == "red"
    assert captured["crop_subject"] == "cropped Water Polo player head/cap frames"
    assert captured["occlusion_hint"] == "water splash, blur, or angle"
    assert "CAP_COLOR_HINT_MISMATCH" not in draft.events[0].review_reasons
    assert draft.events[0].resolved_cap_number == 1
    assert draft.events[0].match_lineup_id is not None


def test_field_player_event_still_blocks_on_a_red_cap_mismatch(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    pass1 = _SingleChunkPass1(
        Pass1ChunkResult(period_events=[_raw_event(CODE_STEAL, 60.0, cap_color_hint="red")])
    )

    class _Resolver(BaseCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None, **_hints):
            return CapResolutionResult(
                resolved_cap_number=7, confidence=0.95, candidate_scores={7: 0.95}
            )

    draft = _orchestrator(tmp_path, None, pass1=pass1, cap_resolver=_Resolver()).run(
        sample_job_request
    )

    assert "CAP_COLOR_HINT_MISMATCH" in draft.events[0].review_reasons
    assert draft.events[0].match_lineup_id is None


# --------------------------------------------------------------------------- factory wiring


def test_factory_builds_the_clip_verifier_from_settings() -> None:
    settings = Settings(
        _env_file=None,
        internal_api_key="k",
        gemini_api_key="g",
        gemini_verifier_model="gemini-4-hydrogen",
        verifier_max_candidates=12,
        verifier_batch_size=5,
        verifier_max_concurrency=3,
    )

    verifier = build_verifier(settings, MockFFmpegClient())  # type: ignore[arg-type]

    assert isinstance(verifier, GeminiClipVerifier)
    assert verifier._max_candidates == 12  # noqa: SLF001
    assert verifier._batch_size == 5  # noqa: SLF001
    assert verifier._max_concurrency == 3  # noqa: SLF001
    assert verifier._model_name == "gemini-4-hydrogen"  # noqa: SLF001


def test_factory_returns_no_verifier_when_disabled() -> None:
    settings = Settings(_env_file=None, internal_api_key="k", verifier_enabled=False)

    assert build_verifier(settings, MockFFmpegClient()) is None  # type: ignore[arg-type]


def test_verifier_settings_defaults_and_bounds() -> None:
    settings = Settings(_env_file=None, internal_api_key="k")

    assert settings.verifier_enabled is True
    assert 3 <= settings.verifier_batch_size <= 5
    assert settings.verifier_max_candidates >= 1
    assert settings.verifier_max_concurrency >= 1
    with pytest.raises(ValueError):
        Settings(_env_file=None, internal_api_key="k", verifier_batch_size=9)
