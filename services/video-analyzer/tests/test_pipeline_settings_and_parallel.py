"""Tests for the infrastructure settings added in #157 and the ordered parallel-map helper."""

from __future__ import annotations

import os
import sys
import threading
import time
from types import SimpleNamespace

import pytest

from video_analyzer.config import Settings
from video_analyzer.gemini.resilience import BreakerReason
from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.cap_resolver import CapResolutionResult, LocalOcrCapResolver
from video_analyzer.pass2.failure_reasons import Pass2FailureReason, failure_reason_for_breaker
from video_analyzer.pass2.lineup_matcher import LineupMatcher
from video_analyzer.pipeline.orchestrator import VideoAnalysisOrchestrator, _CapOutcome
from video_analyzer.pipeline.parallel import ordered_parallel_map
from video_analyzer.schemas.request import AnalysisOptions, MatchSnapshot

_INFRA_ENV_VARS = (
    "PASS1_MAX_CONCURRENCY",
    "PASS2_PHASE_A_MAX_CONCURRENCY",
    "PASS2_PHASE_B_MAX_CONCURRENCY",
    "DETECTOR_MAX_CONCURRENCY",
    "DETECTOR_INPUT_WIDTH",
    "FFMPEG_HWACCEL",
    "FFMPEG_VIDEO_ENCODER",
    "FFMPEG_EXTRACTION_MAX_CONCURRENCY",
    "FFMPEG_EXTRACTION_THREADS",
    "FFMPEG_EXTRACTION_HWACCEL",
    "GEMINI_PASS1_RPM",
    "GEMINI_PASS2_RPM",
    "GEMINI_MAX_RETRIES",
    "GEMINI_RETRY_BASE_SEC",
    "ROBOFLOW_MAX_RETRIES",
    "ROBOFLOW_RETRY_BASE_SEC",
)


def _clear_infra_env(monkeypatch: pytest.MonkeyPatch) -> None:
    """Remove every infra env var regardless of key case (Settings is case-insensitive)."""
    wanted = {name.upper() for name in _INFRA_ENV_VARS}
    for key in list(os.environ):
        if key.upper() in wanted:
            monkeypatch.delenv(key, raising=False)


def test_infrastructure_setting_defaults_match_the_tier_1_profile(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _clear_infra_env(monkeypatch)
    settings = Settings(_env_file=None)

    assert (settings.pass1_max_concurrency, settings.pass2_phase_a_max_concurrency) == (3, 4)
    assert settings.pass2_phase_b_max_concurrency == 4
    assert (settings.detector_max_concurrency, settings.detector_input_width) == (4, 1280)
    assert (settings.ffmpeg_hwaccel, settings.ffmpeg_video_encoder) == ("auto", "auto")
    # Pass 2 4K decoders: capped concurrency, few threads, software decode (RAM benchmark).
    assert settings.ffmpeg_extraction_max_concurrency == 2
    assert settings.ffmpeg_extraction_threads == 2
    assert settings.ffmpeg_extraction_hwaccel == "none"
    assert (settings.gemini_pass1_rpm, settings.gemini_pass2_rpm) == (60, 120)
    assert (settings.gemini_max_retries, settings.gemini_retry_base_sec) == (3, 2.0)
    assert (settings.roboflow_max_retries, settings.roboflow_retry_base_sec) == (3, 1.0)


def test_infrastructure_settings_are_overridable_from_the_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    overrides = {
        "PASS1_MAX_CONCURRENCY": "1",
        "PASS2_PHASE_A_MAX_CONCURRENCY": "2",
        "PASS2_PHASE_B_MAX_CONCURRENCY": "1",
        "DETECTOR_MAX_CONCURRENCY": "1",
        "DETECTOR_INPUT_WIDTH": "960",
        "FFMPEG_HWACCEL": " NONE ",
        "FFMPEG_VIDEO_ENCODER": "libx264",
        "FFMPEG_EXTRACTION_MAX_CONCURRENCY": "1",
        "FFMPEG_EXTRACTION_THREADS": "0",
        "FFMPEG_EXTRACTION_HWACCEL": " AUTO ",
        "GEMINI_PASS1_RPM": "5",
        "GEMINI_PASS2_RPM": "15",
        "GEMINI_MAX_CALLS_PER_JOB": "20",
        "GEMINI_MAX_ESTIMATED_COST_USD": "0.5",
    }
    for name, value in overrides.items():
        monkeypatch.setenv(name, value)
    settings = Settings(_env_file=None)

    assert settings.pass1_max_concurrency == 1
    assert settings.pass2_phase_a_max_concurrency == 2
    assert settings.pass2_phase_b_max_concurrency == 1
    assert settings.detector_max_concurrency == 1
    assert settings.detector_input_width == 960
    assert settings.ffmpeg_hwaccel == "none"
    assert settings.ffmpeg_video_encoder == "libx264"
    assert settings.ffmpeg_extraction_max_concurrency == 1
    assert settings.ffmpeg_extraction_threads == 0
    assert settings.ffmpeg_extraction_hwaccel == "auto"
    assert (settings.gemini_pass1_rpm, settings.gemini_pass2_rpm) == (5, 15)
    assert settings.gemini_max_calls_per_job == 20
    assert settings.gemini_max_estimated_cost_usd == 0.5

    monkeypatch.setenv("PASS1_MAX_CONCURRENCY", "0")
    with pytest.raises(ValueError):
        Settings(_env_file=None)


def test_analysis_options_use_fast_pass2_defaults() -> None:
    options = AnalysisOptions()
    assert options.pass2_window_half_sec == 2.5
    assert options.pass2_sample_fps == 4.0
    # roughly 20 frames per event window
    assert round(options.pass2_window_half_sec * 2 * options.pass2_sample_fps) == 20


def test_lineup_matcher_uses_specific_failure_reason_instead_of_blanket_fallback(
    sample_snapshot: MatchSnapshot,
) -> None:
    definition = next(d for d in sample_snapshot.event_definitions if d.short_name == "G")
    candidate = Pass1EventCandidate(
        video_time_sec=55.0,
        definition=definition,
        period_number=1,
        is_lead_to_goal=False,
        actor_bbox_normalized=[0.4, 0.4, 0.5, 0.6],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.92,
    )
    matcher = LineupMatcher()
    for reason in Pass2FailureReason:
        outcome = matcher.resolve(
            candidate,
            None,
            sample_snapshot,
            min_cap_confidence=0.8,
            pass2_failure_reason=reason.value,
        )
        assert reason.value in outcome.review_reasons
        assert outcome.needs_review is True
        assert outcome.match_lineup_id is None
        if reason is not Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED:
            assert "NO_PLAYER_TRACKLET_MATCHED" not in outcome.review_reasons

    legacy = matcher.resolve(candidate, None, sample_snapshot, min_cap_confidence=0.8)
    assert "NO_PLAYER_TRACKLET_MATCHED" in legacy.review_reasons

    with_result = matcher.resolve(
        candidate,
        CapResolutionResult(resolved_cap_number=7, confidence=0.95, candidate_scores={7: 0.95}),
        sample_snapshot,
        min_cap_confidence=0.8,
        pass2_failure_reason="CAP_RESOLUTION_API_ERROR",
    )
    assert "CAP_RESOLUTION_API_ERROR" not in with_result.review_reasons


def test_breaker_reasons_map_to_distinct_cap_resolution_failures() -> None:
    assert failure_reason_for_breaker(BreakerReason.QUOTA_EXHAUSTED) is (
        Pass2FailureReason.CAP_RESOLUTION_QUOTA_EXHAUSTED
    )
    assert failure_reason_for_breaker(BreakerReason.BUDGET_EXCEEDED) is (
        Pass2FailureReason.CAP_RESOLUTION_BUDGET_EXCEEDED
    )
    assert failure_reason_for_breaker(BreakerReason.API_ERROR) is (
        Pass2FailureReason.CAP_RESOLUTION_API_ERROR
    )
    assert {reason.value for reason in Pass2FailureReason} == {
        "NO_FRAMES_EXTRACTED",
        "NO_PLAYER_TRACKLET_MATCHED",
        "NO_USABLE_CAP_CROPS",
        "CAP_RESOLUTION_API_ERROR",
        "CAP_RESOLUTION_QUOTA_EXHAUSTED",
        "CAP_RESOLUTION_BUDGET_EXCEEDED",
    }


def test_ordered_parallel_map_preserves_input_order_with_staggered_completion() -> None:
    def slow_first(value: int) -> int:
        time.sleep(0.04 * (5 - value))
        return value * 10

    assert ordered_parallel_map(slow_first, [1, 2, 3, 4], max_workers=4) == [10, 20, 30, 40]


def test_ordered_parallel_map_runs_sequentially_on_caller_thread_when_limit_is_one() -> None:
    thread_names: set[str] = set()
    order: list[int] = []

    def record(value: int) -> int:
        thread_names.add(threading.current_thread().name)
        order.append(value)
        return value

    assert ordered_parallel_map(record, [3, 1, 2], max_workers=1) == [3, 1, 2]
    assert order == [3, 1, 2]
    assert thread_names == {threading.current_thread().name}
    assert ordered_parallel_map(record, [], max_workers=4) == []
    assert ordered_parallel_map(record, [9], max_workers=4) == [9]


def test_ordered_parallel_map_is_bounded_and_raises_earliest_real_failure() -> None:
    lock = threading.Lock()
    state = {"active": 0, "max": 0}

    def bounded(value: int) -> int:
        with lock:
            state["active"] += 1
            state["max"] = max(state["max"], state["active"])
        time.sleep(0.03)
        with lock:
            state["active"] -= 1
        return value

    assert ordered_parallel_map(bounded, list(range(8)), max_workers=3) == list(range(8))
    assert state["max"] == 3

    started: list[int] = []

    def failing(value: int) -> int:
        started.append(value)
        if value == 1:
            raise RuntimeError("item 1 failed")
        time.sleep(0.05)
        return value

    with pytest.raises(RuntimeError, match="item 1 failed"):
        ordered_parallel_map(failing, list(range(6)), max_workers=2)
    assert len(started) < 6, "queued tasks are skipped once a sibling has failed"


def test_ordered_parallel_map_runs_before_task_hook_for_every_task() -> None:
    calls: list[int] = []
    lock = threading.Lock()

    def hook() -> None:
        with lock:
            calls.append(1)

    ordered_parallel_map(lambda v: v, [1, 2, 3], max_workers=2, before_task=hook)
    ordered_parallel_map(lambda v: v, [1, 2], max_workers=1, before_task=hook)
    assert len(calls) == 5

    def cancel() -> None:
        raise KeyboardInterrupt

    with pytest.raises(KeyboardInterrupt):
        ordered_parallel_map(lambda v: v, [1, 2, 3], max_workers=2, before_task=cancel)


def test_before_task_failure_aborts_tasks_that_have_not_started() -> None:
    started: list[int] = []
    first_call = threading.Event()
    call_lock = threading.Lock()

    def before_task() -> None:
        with call_lock:
            is_first = not first_call.is_set()
            first_call.set()
        if is_first:
            raise ValueError("cancelled before start")

    def work(item: int) -> int:
        started.append(item)
        time.sleep(0.2)  # keep the sibling worker busy so later tasks start after the abort
        return item

    with pytest.raises(ValueError, match="cancelled before start"):
        ordered_parallel_map(work, range(8), max_workers=2, before_task=before_task)

    assert len(started) < 7, "tasks queued after the failure must be skipped"


def test_task_failure_aborts_siblings_and_raises_the_earliest_failure() -> None:
    started: list[int] = []

    def work(item: int) -> int:
        started.append(item)
        if item == 0:
            raise RuntimeError("boom")
        time.sleep(0.2)
        return item

    with pytest.raises(RuntimeError, match="boom"):
        ordered_parallel_map(work, range(8), max_workers=2)

    assert len(started) < 8


def test_fallback_summary_counts_only_events_that_the_local_ocr_actually_resolved() -> None:
    outcomes = [
        _CapOutcome(used_fallback=True),  # OCR read a confirmed lineup
        _CapOutcome(used_fallback=True),  # OCR found nothing usable
        _CapOutcome(used_fallback=True),  # OCR cap rejected by the cap-color guard
        _CapOutcome(),
    ]
    events = [
        SimpleNamespace(match_lineup_id="lineup-7"),
        SimpleNamespace(match_lineup_id=None),
        SimpleNamespace(match_lineup_id=None),
        SimpleNamespace(match_lineup_id="lineup-3"),
    ]

    messages = VideoAnalysisOrchestrator._fallback_summary(outcomes, events)  # type: ignore[arg-type]

    assert messages == ["Gemini Pass 2 stopped; 1 event(s) resolved with the local OCR fallback."]
    assert VideoAnalysisOrchestrator._fallback_summary(outcomes[2:3], events[2:3]) == []  # type: ignore[arg-type]


def test_local_ocr_reader_is_created_once_under_concurrent_first_use(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    created: list[object] = []
    created_lock = threading.Lock()

    class SlowReader:
        def __init__(self, languages, gpu=False, verbose=False) -> None:
            time.sleep(0.05)  # widen the window in which a second thread could also initialize
            with created_lock:
                created.append(self)

    monkeypatch.setitem(sys.modules, "easyocr", SimpleNamespace(Reader=SlowReader))
    resolver = LocalOcrCapResolver()
    readers: list[object] = []

    threads = [
        threading.Thread(target=lambda: readers.append(resolver._get_reader())) for _ in range(8)
    ]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()

    assert len(created) == 1
    assert len({id(reader) for reader in readers}) == 1
