"""End-to-end pipeline, sample snapshot, FastAPI, and CLI contract tests."""

from __future__ import annotations

import json
import tempfile
import threading
import time
from pathlib import Path
from types import SimpleNamespace
from uuid import uuid4

import cv2
import numpy as np
import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from video_analyzer.api.app import create_app
from video_analyzer.cli import main as cli_main
from video_analyzer.config import Settings
from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.gemini.resilience import (
    BreakerReason,
    GeminiCallBlockedError,
    GeminiResilience,
)
from video_analyzer.media.ffmpeg_client import ExtractedFrame, VideoChunk, VideoProbeResult
from video_analyzer.pass1.gemini_client import BasePass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawAnchor, Pass1RawEvent
from video_analyzer.pass2.cap_resolver import (
    BaseCapResolver,
    CapResolutionError,
    CapResolutionResult,
    LocalOcrCapResolver,
)
from video_analyzer.pipeline.orchestrator import JobCancelledError, VideoAnalysisOrchestrator
from video_analyzer.schemas.draft import GeminiTokenUsage, TimeAnchorType
from video_analyzer.schemas.request import AnalyzeJobRequest, MatchSnapshot


class MockFFmpegClient:
    def resolve_source_path(self, source, work_dir: Path, cancel_event=None) -> Path:
        return Path(source.local_path or "dummy.mp4")

    def probe(self, video_path: Path, cancel_event=None) -> VideoProbeResult:
        return VideoProbeResult(
            duration_sec=240.0,
            width=3840,
            height=2160,
            fps=30.0,
            has_audio=True,
            codec_name="h264",
        )

    def create_pass1_chunks(self, video_path, probe, output_dir, **kwargs):
        output_dir.mkdir(parents=True, exist_ok=True)
        chunk_file = output_dir / "chunk_0.mp4"
        chunk_file.write_bytes(b"mp4")
        return [VideoChunk(index=0, start_sec=0.0, end_sec=240.0, path=chunk_file)]

    def extract_window_frames(self, video_path, center_sec, **kwargs):
        frame = np.zeros((720, 1280, 3), dtype=np.uint8)
        cv2.putText(frame, "7", (630, 310), cv2.FONT_HERSHEY_SIMPLEX, 1.5, (255, 255, 255), 3)
        return [
            ExtractedFrame(index=0, video_time_sec=center_sec - 0.1, image_bgr=frame),
            ExtractedFrame(index=1, video_time_sec=center_sec, image_bgr=frame.copy()),
            ExtractedFrame(index=2, video_time_sec=center_sec + 0.1, image_bgr=frame.copy()),
        ]


class MockPass1Analyzer(BasePass1Analyzer):
    def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
        return Pass1ChunkResult(
            time_anchors=[
                Pass1RawAnchor(
                    time_sec=5.0,
                    period_number=1,
                    type=TimeAnchorType.PERIOD_START,
                    confidence=0.95,
                    evidence="Sprint whistle",
                ),
                Pass1RawAnchor(
                    time_sec=220.0,
                    period_number=1,
                    type=TimeAnchorType.PERIOD_END,
                    confidence=0.96,
                    evidence="Period horn",
                ),
            ],
            period_events=[
                Pass1RawEvent(
                    time_sec=45.0,
                    prompt_code="EVT_01_G_POS",
                    period_number=1,
                    is_lead_to_goal=True,
                    actor_bbox_normalized=[0.45, 0.35, 0.55, 0.65],
                    pool_zone_hint="center_5m",
                    confidence=0.93,
                ),
                Pass1RawEvent(
                    time_sec=110.0,
                    prompt_code="EVT_14_GC_NEG",
                    period_number=1,
                    is_lead_to_goal=True,
                    actor_bbox_normalized=[0.45, 0.35, 0.55, 0.65],
                    pool_zone_hint="home_goal",
                    confidence=0.86,
                ),
            ],
        )


# Fixed rates keep cost arithmetic independent of the shipped default price list.
_FIXED_HYDROGEN_PRICING = {"input": 0.50, "output": 3.00}


class MockDetector(BaseObjectDetector):
    def provider_info(self) -> dict[str, str]:
        return {"provider": "roboflow", "model": "waterpolo-salvasc/5"}

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        h, w = frame_bgr.shape[:2]
        return [
            Detection(
                bbox_xyxy=(0.45 * w, 0.35 * h, 0.55 * w, 0.65 * h),
                confidence=0.91,
                role=CanonicalRole.PLAYER,
                raw_class_name="White team",
            )
        ]


class MockCapResolver(BaseCapResolver):
    """Order-scripted resolver: tests using it must set ``pass2_phase_b_max_concurrency=1``."""

    def __init__(self) -> None:
        self.calls = 0

    def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
        self.calls += 1
        if self.calls == 1:
            return CapResolutionResult(
                resolved_cap_number=7,
                confidence=0.92,
                candidate_scores={7: 0.92},
                reasoning="Cap #7 clearly readable",
            )
        # Second event: ambiguous/low confidence -> must return matchLineupId=null
        return CapResolutionResult(
            resolved_cap_number=1,
            confidence=0.45,
            candidate_scores={1: 0.45, 13: 0.38},
            reasoning="Splash obscured cap number",
        )


def _build_mock_orchestrator(work_dir: Path | None = None) -> VideoAnalysisOrchestrator:
    resolved_work_dir = str(work_dir) if work_dir is not None else tempfile.gettempdir()
    settings = Settings(
        _env_file=None,
        detector_provider="roboflow",
        detector_model_path_or_id="waterpolo-salvasc/5",
        internal_api_key="secret-test-key",
        work_dir=resolved_work_dir,
        pass2_phase_b_max_concurrency=1,
    )
    return VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=MockPass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=MockCapResolver(),
    )


def test_sample_snapshot_contains_all_34_seeded_waterpolo_definitions(
    sample_snapshot: MatchSnapshot,
) -> None:
    assert len(sample_snapshot.event_definitions) == 34
    goals = [d for d in sample_snapshot.event_definitions if d.name == "Goal"]
    assert len(goals) == 2
    assert {g.short_name for g in goals} == {"G", "GC"}
    assert len({g.event_definition_id for g in goals}) == 2


def test_orchestrator_end_to_end_draft_contract(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    orchestrator = _build_mock_orchestrator(work_dir=tmp_path)
    draft = orchestrator.run(sample_job_request)
    payload = draft.model_dump(mode="json", by_alias=True)

    assert payload["schemaVersion"] == "1.0"
    assert payload["playerPresences"] == []
    assert len(payload["timeAnchors"]) == 2
    assert payload["timeAnchors"][0]["type"] == "PeriodStart"
    assert payload["timeAnchors"][1]["type"] == "PeriodEnd"

    events = payload["events"]
    assert len(events) == 2
    assert events[0]["promptCode"] == "EVT_01_G_POS"
    assert events[0]["isPositive"] is True
    assert events[0]["resolvedCapNumber"] == 7
    assert events[0]["matchLineupId"] is not None
    assert events[0]["needsReview"] is False

    assert events[1]["promptCode"] == "EVT_14_GC_NEG"
    assert events[1]["isPositive"] is False
    assert events[1]["matchLineupId"] is None
    assert events[1]["needsReview"] is True
    assert len(events[1]["candidateLineupIds"]) == 2
    assert payload["diagnostics"]["resolvedActorCount"] == 1
    assert payload["diagnostics"]["unresolvedActorCount"] == 1


def test_fastapi_endpoints_auth_and_job_lifecycle(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    settings = Settings(_env_file=None, internal_api_key="secret-test-key")
    app = create_app(settings=settings, orchestrator=_build_mock_orchestrator(work_dir=tmp_path))

    with TestClient(app) as client:
        health_resp = client.get("/health")
        assert health_resp.status_code == 200
        assert health_resp.json()["detectorProvider"] == "roboflow"

        body = sample_job_request.model_dump(mode="json", by_alias=True)
        unauth = client.post("/v1/analysis/jobs", json=body)
        assert unauth.status_code == 401

        submit = client.post(
            "/v1/analysis/jobs",
            json=body,
            headers={"X-Internal-Api-Key": "secret-test-key"},
        )
        assert submit.status_code == 202
        job_id = submit.json()["jobId"]

        final_payload = None
        for _ in range(30):
            poll = client.get(
                f"/v1/analysis/jobs/{job_id}",
                headers={"X-Internal-Api-Key": "secret-test-key"},
            )
            assert poll.status_code == 200
            data = poll.json()
            if data["status"] == "completed":
                final_payload = data
                break
            time.sleep(0.05)

        assert final_payload is not None
        assert len(final_payload["draft"]["events"]) == 2

        cancel_resp = client.delete(
            f"/v1/analysis/jobs/{job_id}",
            headers={"X-Internal-Api-Key": "secret-test-key"},
        )
        assert cancel_resp.status_code == 204


def test_cli_analyze_writes_draft_json(
    monkeypatch,
    tmp_path: Path,
) -> None:
    video = tmp_path / "sample.mp4"
    video.write_bytes(b"video")
    out_file = tmp_path / "out" / "draft.json"
    snapshot_file = (
        Path(__file__).resolve().parents[1] / "sample_snapshots" / "waterpolo_default_snapshot.json"
    )

    monkeypatch.setattr(
        "video_analyzer.cli.build_default_orchestrator",
        lambda settings: _build_mock_orchestrator(work_dir=tmp_path),
    )

    exit_code = cli_main(
        [
            "analyze",
            "--video",
            str(video),
            "--snapshot",
            str(snapshot_file),
            "--out",
            str(out_file),
        ]
    )
    assert exit_code == 0
    written = json.loads(out_file.read_text(encoding="utf-8"))
    assert len(written["events"]) == 2
    assert written["videoMetadata"]["width"] == 3840


def test_request_schemas_validate_unique_definition_ids_and_team_consistency(
    sample_snapshot: MatchSnapshot,
    sample_job_request: AnalyzeJobRequest,
) -> None:
    dup_defs = [
        sample_snapshot.event_definitions[0],
        sample_snapshot.event_definitions[1].model_copy(
            update={"event_definition_id": sample_snapshot.event_definitions[0].event_definition_id}
        ),
    ]
    with pytest.raises(ValidationError, match="eventDefinitionId"):
        MatchSnapshot.model_validate(
            {
                **sample_snapshot.model_dump(by_alias=True),
                "eventDefinitions": [d.model_dump(by_alias=True) for d in dup_defs],
            }
        )

    mismatched_payload = {
        **sample_job_request.model_dump(by_alias=True),
        "trackedTeamId": str(uuid4()),
    }
    with pytest.raises(ValidationError, match="trackedTeamId"):
        AnalyzeJobRequest.model_validate(mismatched_payload)


def test_orchestrator_streams_pass2_frames_without_temp_dirs_and_propagates_tied_tracks(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    work_root = tmp_path / "work"
    work_root.mkdir()
    extract_kwargs: list[dict] = []
    leaked_dirs: list[Path] = []

    class StreamingFFmpeg(MockFFmpegClient):
        def extract_window_frames(self, video_path, center_sec, **kwargs):
            extract_kwargs.append(kwargs)
            leaked_dirs.extend(work_root.rglob("pass2_win_*"))
            return super().extract_window_frames(video_path, center_sec, **kwargs)

    class TiedDetector(BaseObjectDetector):
        def provider_info(self) -> dict[str, str]:
            return {"provider": "mock", "model": "tied"}

        def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
            h, w = frame_bgr.shape[:2]
            box = (0.45 * w, 0.35 * h, 0.55 * w, 0.65 * h)
            return [
                Detection(box, 0.91, CanonicalRole.PLAYER, "White team"),
                Detection(box, 0.91, CanonicalRole.PLAYER, "White team"),
            ]

    orchestrator = VideoAnalysisOrchestrator(
        settings=Settings(
            _env_file=None,
            internal_api_key="test-key",
            work_dir=str(work_root),
            pass2_phase_b_max_concurrency=1,
        ),
        ffmpeg_client=StreamingFFmpeg(),  # type: ignore[arg-type]
        pass1_analyzer=MockPass1Analyzer(),
        detector=TiedDetector(),
        cap_resolver=MockCapResolver(),
    )

    draft = orchestrator.run(sample_job_request)
    assert len(extract_kwargs) == 2
    assert all("work_dir" not in kwargs for kwargs in extract_kwargs)
    assert leaked_dirs == []
    assert all(evt.match_lineup_id is None for evt in draft.events)
    assert all(evt.needs_review is True for evt in draft.events)
    assert all("TIED_ACTOR_TRACKLET_MATCH" in evt.review_reasons for evt in draft.events)


def test_build_mock_orchestrator_ignores_operator_env_and_work_dir(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    sample_job_request: AnalyzeJobRequest,
) -> None:
    bad_dir = tmp_path / "nonexistent_env_work_dir"
    (tmp_path / ".env").write_text(f"WORK_DIR={bad_dir}\n", encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setenv("WORK_DIR", str(bad_dir))

    orchestrator = _build_mock_orchestrator()
    assert orchestrator._settings.work_dir == tempfile.gettempdir()
    draft = orchestrator.run(sample_job_request)
    assert len(draft.events) == 2


def test_orchestrator_flags_cap_color_hint_mismatch_across_resolution_paths(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    class ColorHintPass1Analyzer(BasePass1Analyzer):
        def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
            return Pass1ChunkResult(
                time_anchors=[
                    Pass1RawAnchor(
                        time_sec=5.0,
                        period_number=1,
                        type=TimeAnchorType.PERIOD_START,
                        confidence=0.95,
                    ),
                    Pass1RawAnchor(
                        time_sec=220.0,
                        period_number=1,
                        type=TimeAnchorType.PERIOD_END,
                        confidence=0.95,
                    ),
                ],
                period_events=[
                    Pass1RawEvent(
                        time_sec=40.0,
                        prompt_code="EVT_01_G_POS",
                        period_number=1,
                        actor_bbox_normalized=[0.45, 0.35, 0.55, 0.65],
                        cap_color_hint="Blue",
                        confidence=0.94,
                    ),
                    Pass1RawEvent(
                        time_sec=90.0,
                        prompt_code="EVT_02_ST_POS",
                        period_number=1,
                        actor_bbox_normalized=[0.45, 0.35, 0.55, 0.65],
                        cap_color_hint="  WHITE ",
                        confidence=0.94,
                    ),
                ],
            )

    seen_color_hints: list[str | None] = []

    class RecordingCapResolver(BaseCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            seen_color_hints.append(cap_color_hint)
            return CapResolutionResult(
                resolved_cap_number=7,
                confidence=0.95,
                candidate_scores={7: 0.95},
                reasoning="Sharp cap #7",
            )

    settings = Settings(_env_file=None, internal_api_key="test-key", work_dir=str(tmp_path))

    # 1. Gemini/mock resolver path: mismatch blocks auto-confirm, normalized match succeeds
    orch_mock = VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=ColorHintPass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=RecordingCapResolver(),
    )
    draft_mock = orch_mock.run(sample_job_request)
    assert seen_color_hints == ["white", "white"]
    mismatch_evt, matched_evt = draft_mock.events
    assert mismatch_evt.match_lineup_id is None
    assert mismatch_evt.resolved_cap_number is None
    assert mismatch_evt.needs_review is True
    assert "CAP_COLOR_HINT_MISMATCH" in mismatch_evt.review_reasons
    assert [c.cap_number for c in mismatch_evt.candidate_lineup_ids] == [7]
    assert matched_evt.match_lineup_id is not None
    assert matched_evt.resolved_cap_number == 7
    assert matched_evt.needs_review is False
    assert draft_mock.diagnostics.resolved_actor_count == 1
    assert draft_mock.diagnostics.unresolved_actor_count == 1

    # 2. LocalOcrCapResolver path: mismatch still blocks auto-confirm
    class FakeOcrReader:
        @staticmethod
        def readtext(image, allowlist: str = ""):
            return [([], "7", 0.98)]

    orch_ocr = VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=ColorHintPass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=LocalOcrCapResolver(reader=FakeOcrReader()),
    )
    draft_ocr = orch_ocr.run(sample_job_request)
    assert draft_ocr.events[0].match_lineup_id is None
    assert draft_ocr.events[0].resolved_cap_number is None
    assert draft_ocr.events[0].needs_review is True
    assert "CAP_COLOR_HINT_MISMATCH" in draft_ocr.events[0].review_reasons
    assert draft_ocr.events[1].resolved_cap_number == 7
    assert draft_ocr.events[1].needs_review is False

    # 3. skip_pass2=True path: mismatch is also flagged in review_reasons
    skip_req = sample_job_request.model_copy(
        update={
            "analysis_options": sample_job_request.analysis_options.model_copy(
                update={"skip_pass2": True}
            )
        }
    )
    draft_skip = orch_mock.run(skip_req)
    assert draft_skip.events[0].match_lineup_id is None
    assert draft_skip.events[0].needs_review is True
    assert "CAP_COLOR_HINT_MISMATCH" in draft_skip.events[0].review_reasons
    assert "CAP_COLOR_HINT_MISMATCH" not in draft_skip.events[1].review_reasons


def test_orchestrator_checks_cancellation_before_reporting_completed(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    cancel_event = threading.Event()
    reported: list[tuple[str, int]] = []

    class LateCancellingDetector(MockDetector):
        def provider_info(self) -> dict[str, str]:
            cancel_event.set()
            return super().provider_info()

    orchestrator = VideoAnalysisOrchestrator(
        settings=Settings(
            _env_file=None,
            internal_api_key="test-key",
            work_dir=str(tmp_path),
            pass2_phase_b_max_concurrency=1,
        ),
        ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=MockPass1Analyzer(),
        detector=LateCancellingDetector(),
        cap_resolver=MockCapResolver(),
    )

    with pytest.raises(JobCancelledError):
        orchestrator.run(
            sample_job_request,
            cancel_event=cancel_event,
            progress_callback=lambda stage, pct: reported.append((stage, pct)),
        )

    assert ("completed", 100) not in reported


def test_orchestrator_aggregates_gemini_usage_and_cost_in_diagnostics_and_cli(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    class UsagePass1Analyzer(MockPass1Analyzer):
        def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
            base = super().analyze_chunk(snapshot, chunk)
            return base.model_copy(
                update={
                    "token_usage": GeminiTokenUsage(
                        call_count=1,
                        prompt_tokens=200_000,
                        candidates_tokens=10_000,
                        thoughts_tokens=5_000,
                        cached_content_tokens=20_000,
                        total_tokens=215_000,
                    )
                }
            )

    class UsageCapResolver(MockCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            base = super().resolve(selected_crops, allowed_cap_numbers, cap_color_hint)
            return base.model_copy(
                update={
                    "token_usage": GeminiTokenUsage(
                        call_count=1,
                        prompt_tokens=50_000,
                        candidates_tokens=2_000,
                        thoughts_tokens=1_000,
                        cached_content_tokens=0,
                        total_tokens=53_000,
                    )
                }
            )

    settings = Settings(
        _env_file=None,
        internal_api_key="test-key",
        work_dir=str(tmp_path),
        gemini_pass1_model="gemini-4-hydrogen",
        gemini_pass2_model="gemini-4-hydrogen",
        gemini_model_pricing_usd_per_1m={"gemini-4-hydrogen": _FIXED_HYDROGEN_PRICING},
        pass2_phase_b_max_concurrency=1,
    )
    orchestrator = VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=UsagePass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=UsageCapResolver(),
    )

    draft = orchestrator.run(sample_job_request)
    payload = draft.model_dump(mode="json", by_alias=True)
    gemini_usage = payload["diagnostics"]["geminiUsage"]

    assert payload["modelMetadata"]["pass1VlmModel"] == "gemini-4-hydrogen"
    assert gemini_usage["pass1"]["model"] == "gemini-4-hydrogen"
    assert gemini_usage["pass1"]["usage"]["callCount"] == 1
    assert gemini_usage["pass1"]["usage"]["promptTokens"] == 200_000
    assert gemini_usage["pass2"]["usage"]["callCount"] == 2
    assert gemini_usage["pass2"]["usage"]["promptTokens"] == 100_000
    assert gemini_usage["total"]["callCount"] == 3
    assert gemini_usage["total"]["promptTokens"] == 300_000
    assert gemini_usage["total"]["candidatesTokens"] == 14_000
    assert gemini_usage["total"]["thoughtsTokens"] == 7_000
    assert gemini_usage["total"]["cachedContentTokens"] == 20_000
    assert gemini_usage["total"]["totalTokens"] == 321_000

    # 300k * $0.50 / 1M = $0.15; (14k + 7k) * $3.00 / 1M = $0.063; total = $0.213
    est_cost = gemini_usage["estimatedCost"]
    assert est_cost["isEstimate"] is True
    assert est_cost["pricingConfigured"] is True
    assert est_cost["inputCostUsd"] == pytest.approx(0.15)
    assert est_cost["outputCostUsd"] == pytest.approx(0.063)
    assert est_cost["totalCostUsd"] == pytest.approx(0.213)

    video = tmp_path / "sample.mp4"
    video.write_bytes(b"video")
    out_file = tmp_path / "out" / "draft_usage.json"
    snapshot_file = (
        Path(__file__).resolve().parents[1] / "sample_snapshots" / "waterpolo_default_snapshot.json"
    )
    monkeypatch.setattr(
        "video_analyzer.cli.build_default_orchestrator",
        lambda _settings: VideoAnalysisOrchestrator(
            settings=settings,
            ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
            pass1_analyzer=UsagePass1Analyzer(),
            detector=MockDetector(),
            cap_resolver=UsageCapResolver(),
        ),
    )
    exit_code = cli_main(
        [
            "analyze",
            "--video",
            str(video),
            "--snapshot",
            str(snapshot_file),
            "--out",
            str(out_file),
        ]
    )
    assert exit_code == 0
    stdout = capsys.readouterr().out
    assert "Gemini usage: calls=3" in stdout
    assert "~$0.213000 USD" in stdout


def test_orchestrator_records_pass2_usage_once_on_recovered_parse_failure(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    class FailingParseCapResolver(BaseCapResolver):
        def __init__(self) -> None:
            self.calls = 0

        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            self.calls += 1
            if self.calls == 1:
                raise CapResolutionError(
                    "Invalid JSON payload",
                    token_usage=GeminiTokenUsage(
                        call_count=1,
                        prompt_tokens=50_000,
                        candidates_tokens=2_000,
                        thoughts_tokens=None,
                        total_tokens=52_000,
                    ),
                )
            return CapResolutionResult(
                resolved_cap_number=7,
                confidence=0.95,
                candidate_scores={7: 0.95},
                reasoning="Clear cap #7",
                token_usage=GeminiTokenUsage(
                    call_count=1,
                    prompt_tokens=60_000,
                    candidates_tokens=3_000,
                    thoughts_tokens=None,
                    total_tokens=63_000,
                ),
            )

    settings = Settings(
        _env_file=None,
        internal_api_key="test-key",
        work_dir=str(tmp_path),
        gemini_pass1_model="gemini-4-hydrogen",
        gemini_pass2_model="gemini-4-hydrogen",
        gemini_model_pricing_usd_per_1m={"gemini-4-hydrogen": _FIXED_HYDROGEN_PRICING},
        pass2_phase_b_max_concurrency=1,
    )
    orchestrator = VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=MockPass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=FailingParseCapResolver(),
    )

    draft = orchestrator.run(sample_job_request)
    # First event failed cap parse -> unresolved; second succeeded -> resolved
    assert draft.events[0].match_lineup_id is None
    assert draft.events[0].needs_review is True
    assert draft.events[1].match_lineup_id is not None
    assert any("cap resolution failed" in w for w in draft.diagnostics.warnings)

    # Both Pass 2 calls' token usage are recorded exactly once
    p2_usage = draft.diagnostics.gemini_usage.pass2.usage
    assert p2_usage.call_count == 2
    assert p2_usage.prompt_tokens == 110_000
    assert p2_usage.candidates_tokens == 5_000
    assert p2_usage.thoughts_tokens is None
    assert p2_usage.total_tokens == 115_000
    assert draft.diagnostics.gemini_usage.total.call_count == 2
    assert draft.diagnostics.gemini_usage.estimated_cost is not None
    assert draft.diagnostics.gemini_usage.estimated_cost.total_cost_usd == pytest.approx(0.07)


# ---------------------------------------------------------------------------------------------
# Issue #157: parallel Pass 1, decoupled Pass 2 phases, breaker handling, failure taxonomy
# ---------------------------------------------------------------------------------------------

_EVENT_TIMES_SEC = (20.0, 45.0, 70.0, 95.0, 120.0, 145.0)


class _ManyEventsPass1Analyzer(BasePass1Analyzer):
    """Returns one confident goal event per entry in ``_EVENT_TIMES_SEC`` (single chunk)."""

    def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
        return Pass1ChunkResult(
            time_anchors=[],
            period_events=[
                Pass1RawEvent(
                    time_sec=time_sec,
                    prompt_code="EVT_01_G_POS",
                    period_number=1,
                    actor_bbox_normalized=[0.45, 0.35, 0.55, 0.65],
                    confidence=0.93,
                )
                for time_sec in _EVENT_TIMES_SEC
            ],
        )


class _CountingFFmpeg(MockFFmpegClient):
    """Mock ffmpeg that records Pass 2 extraction concurrency and the kwargs it receives."""

    def __init__(self, delay_sec: float = 0.0, frames_enabled: bool = True) -> None:
        self._lock = threading.Lock()
        self._active = 0
        self.max_active = 0
        self.extract_calls: list[dict] = []
        self._delay_sec = delay_sec
        self._frames_enabled = frames_enabled

    def extract_window_frames(self, video_path, center_sec, **kwargs):
        with self._lock:
            self._active += 1
            self.max_active = max(self.max_active, self._active)
            self.extract_calls.append({"center_sec": center_sec, **kwargs})
        try:
            time.sleep(self._delay_sec)
            if not self._frames_enabled:
                return []
            return super().extract_window_frames(video_path, center_sec, **kwargs)
        finally:
            with self._lock:
                self._active -= 1


class _ConcurrencyProbeDetector(MockDetector):
    """Detector that measures simultaneous ``detect`` calls."""

    def __init__(self, concurrent: bool, delay_sec: float = 0.02) -> None:
        self.supports_concurrent_inference = concurrent
        self._lock = threading.Lock()
        self._active = 0
        self.max_active = 0
        self._delay_sec = delay_sec

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        with self._lock:
            self._active += 1
            self.max_active = max(self.max_active, self._active)
        try:
            time.sleep(self._delay_sec)
            return super().detect(frame_bgr)
        finally:
            with self._lock:
                self._active -= 1


class _AlwaysSevenCapResolver(BaseCapResolver):
    """Thread-safe resolver returning cap #7 and tracking its own concurrency."""

    def __init__(self, delay_sec: float = 0.0) -> None:
        self._lock = threading.Lock()
        self._active = 0
        self.max_active = 0
        self.calls = 0
        self._delay_sec = delay_sec

    def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
        with self._lock:
            self.calls += 1
            self._active += 1
            self.max_active = max(self.max_active, self._active)
        try:
            time.sleep(self._delay_sec)
            return CapResolutionResult(
                resolved_cap_number=7,
                confidence=0.95,
                candidate_scores={7: 0.95},
                reasoning="Cap #7",
            )
        finally:
            with self._lock:
                self._active -= 1


def _orchestrator_with(
    tmp_path: Path,
    *,
    ffmpeg=None,
    detector=None,
    cap_resolver=None,
    pass1=None,
    fallback_cap_resolver=None,
    crop_selector=None,
    **settings_overrides,
) -> VideoAnalysisOrchestrator:
    settings_overrides.setdefault("work_dir", str(tmp_path))
    settings = Settings(_env_file=None, internal_api_key="test-key", **settings_overrides)
    return VideoAnalysisOrchestrator(
        settings=settings,
        ffmpeg_client=ffmpeg or MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=pass1 or _ManyEventsPass1Analyzer(),
        detector=detector or MockDetector(),
        cap_resolver=cap_resolver or _AlwaysSevenCapResolver(),
        fallback_cap_resolver=fallback_cap_resolver,
        crop_selector=crop_selector,
    )


def test_pass1_parallel_merge_is_deterministic_and_concurrency_one_is_sequential(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    class FourChunkFFmpeg(MockFFmpegClient):
        def create_pass1_chunks(self, video_path, probe, output_dir, **kwargs):
            output_dir.mkdir(parents=True, exist_ok=True)
            chunks = []
            for idx in range(4):
                path = output_dir / f"chunk_{idx}.mp4"
                path.write_bytes(b"mp4")
                chunks.append(VideoChunk(idx, idx * 50.0, idx * 50.0 + 60.0, path))
            return chunks

    class StaggeredAnalyzer(BasePass1Analyzer):
        """Earlier chunks finish LAST, so completion order differs from chunk order."""

        def __init__(self) -> None:
            self.lock = threading.Lock()
            self.active = 0
            self.max_active = 0
            self.completion_order: list[int] = []

        def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
            with self.lock:
                self.active += 1
                self.max_active = max(self.max_active, self.active)
            time.sleep(0.05 * (4 - chunk.index))
            with self.lock:
                self.active -= 1
                self.completion_order.append(chunk.index)
            return Pass1ChunkResult(
                time_anchors=[],
                period_events=[
                    Pass1RawEvent(
                        time_sec=5.0,
                        prompt_code="EVT_01_G_POS",
                        period_number=1,
                        confidence=0.9,
                    )
                ],
            )

    request = sample_job_request.model_copy(
        update={
            "analysis_options": sample_job_request.analysis_options.model_copy(
                update={"skip_pass2": True}
            )
        }
    )

    sequential = StaggeredAnalyzer()
    seq_draft = _orchestrator_with(
        tmp_path,
        ffmpeg=FourChunkFFmpeg(),
        pass1=sequential,
        pass1_max_concurrency=1,
    ).run(request)
    assert sequential.max_active == 1
    assert sequential.completion_order == [0, 1, 2, 3]

    parallel = StaggeredAnalyzer()
    par_draft = _orchestrator_with(
        tmp_path,
        ffmpeg=FourChunkFFmpeg(),
        pass1=parallel,
        pass1_max_concurrency=3,
    ).run(request)
    assert 2 <= parallel.max_active <= 3
    assert parallel.completion_order != [0, 1, 2, 3], "tasks really completed out of order"

    def summarize(draft):
        return [(round(e.video_time_sec, 3), e.prompt_code) for e in draft.events]

    assert (
        summarize(par_draft)
        == summarize(seq_draft)
        == [
            (5.0, "EVT_01_G_POS"),
            (55.0, "EVT_01_G_POS"),
            (105.0, "EVT_01_G_POS"),
            (155.0, "EVT_01_G_POS"),
        ]
    )
    assert par_draft.diagnostics.pass1_chunks_processed == 4


def test_pass1_parallel_failure_and_cancellation_propagate_deterministically(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    class TwoChunkFFmpeg(MockFFmpegClient):
        def create_pass1_chunks(self, video_path, probe, output_dir, **kwargs):
            output_dir.mkdir(parents=True, exist_ok=True)
            return [
                VideoChunk(i, i * 50.0, i * 50.0 + 60.0, output_dir / f"c{i}.mp4") for i in range(3)
            ]

    class FailingAnalyzer(BasePass1Analyzer):
        def analyze_chunk(self, snapshot, chunk):
            if chunk.index == 1:
                raise RuntimeError("chunk 1 exploded")
            time.sleep(0.05)
            return Pass1ChunkResult(time_anchors=[], period_events=[])

    orchestrator = _orchestrator_with(
        tmp_path, ffmpeg=TwoChunkFFmpeg(), pass1=FailingAnalyzer(), pass1_max_concurrency=3
    )
    with pytest.raises(RuntimeError, match="chunk 1 exploded"):
        orchestrator.run(sample_job_request)

    cancel_event = threading.Event()

    class CancellingAnalyzer(BasePass1Analyzer):
        def analyze_chunk(self, snapshot, chunk):
            cancel_event.set()
            return Pass1ChunkResult(time_anchors=[], period_events=[])

    cancelling = _orchestrator_with(
        tmp_path, ffmpeg=TwoChunkFFmpeg(), pass1=CancellingAnalyzer(), pass1_max_concurrency=1
    )
    with pytest.raises(JobCancelledError):
        cancelling.run(sample_job_request, cancel_event=cancel_event)


def test_global_detector_semaphore_caps_concurrency_across_all_phase_a_tasks(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    remote = _ConcurrencyProbeDetector(concurrent=True)
    ffmpeg = _CountingFFmpeg(delay_sec=0.02)
    draft = _orchestrator_with(
        tmp_path,
        ffmpeg=ffmpeg,
        detector=remote,
        pass2_phase_a_max_concurrency=6,
        ffmpeg_extraction_max_concurrency=6,
        detector_max_concurrency=2,
    ).run(sample_job_request)

    assert len(draft.events) == len(_EVENT_TIMES_SEC)
    assert ffmpeg.max_active > 2, "Phase A tasks overlap more than the detector limit"
    assert remote.max_active == 2, "...but detector calls never exceed the global semaphore"

    local = _ConcurrencyProbeDetector(concurrent=False)
    _orchestrator_with(
        tmp_path,
        ffmpeg=_CountingFFmpeg(),
        detector=local,
        pass2_phase_a_max_concurrency=6,
        detector_max_concurrency=4,
    ).run(sample_job_request)
    assert local.max_active == 1, "local providers are always serialized"


def test_phase_a_and_phase_b_concurrency_are_independently_configurable(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    ffmpeg = _CountingFFmpeg(delay_sec=0.03)
    resolver = _AlwaysSevenCapResolver(delay_sec=0.03)
    _orchestrator_with(
        tmp_path,
        ffmpeg=ffmpeg,
        cap_resolver=resolver,
        pass2_phase_a_max_concurrency=1,
        pass2_phase_b_max_concurrency=3,
    ).run(sample_job_request)
    assert ffmpeg.max_active == 1
    assert 2 <= resolver.max_active <= 3

    ffmpeg = _CountingFFmpeg(delay_sec=0.03)
    resolver = _AlwaysSevenCapResolver(delay_sec=0.03)
    _orchestrator_with(
        tmp_path,
        ffmpeg=ffmpeg,
        cap_resolver=resolver,
        pass2_phase_a_max_concurrency=3,
        ffmpeg_extraction_max_concurrency=3,
        pass2_phase_b_max_concurrency=1,
    ).run(sample_job_request)
    assert 2 <= ffmpeg.max_active <= 3
    assert resolver.max_active == 1


def test_breaker_trip_preserves_phase_a_and_marks_remaining_events_explicitly(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    class QuotaTrippingResolver(BaseCapResolver):
        def __init__(self) -> None:
            self.calls = 0

        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            self.calls += 1
            if self.calls == 1:
                return CapResolutionResult(
                    resolved_cap_number=7,
                    confidence=0.95,
                    candidate_scores={7: 0.95},
                    reasoning="ok",
                )
            raise GeminiCallBlockedError("pass2", BreakerReason.QUOTA_EXHAUSTED, "daily quota")

    ffmpeg = _CountingFFmpeg()
    resolver = QuotaTrippingResolver()
    draft = _orchestrator_with(
        tmp_path,
        ffmpeg=ffmpeg,
        cap_resolver=resolver,
        pass2_phase_b_max_concurrency=1,
    ).run(sample_job_request)

    assert len(ffmpeg.extract_calls) == len(_EVENT_TIMES_SEC), "Phase A ran for every event"
    assert resolver.calls == 2, "no Gemini calls are attempted once the breaker is open"
    assert draft.events[0].resolved_cap_number == 7
    for event in draft.events[1:]:
        assert event.match_lineup_id is None
        assert event.needs_review is True
        assert "CAP_RESOLUTION_QUOTA_EXHAUSTED" in event.review_reasons
        assert "NO_PLAYER_TRACKLET_MATCHED" not in event.review_reasons
    assert any("left unresolved" in w for w in draft.diagnostics.warnings)
    assert draft.diagnostics.pass2_windows_processed == len(_EVENT_TIMES_SEC)
    assert draft.diagnostics.timings_sec["pass2ExtractAndTrack"] > 0


def test_breaker_trip_falls_back_to_local_ocr_when_available(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    class BlockedResolver(BaseCapResolver):
        calls = 0

        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            BlockedResolver.calls += 1
            raise GeminiCallBlockedError("pass2", BreakerReason.QUOTA_EXHAUSTED, "daily quota")

    class FakeOcrReader:
        @staticmethod
        def readtext(image, allowlist: str = ""):
            return [([], "7", 0.98)]

    draft = _orchestrator_with(
        tmp_path,
        cap_resolver=BlockedResolver(),
        fallback_cap_resolver=LocalOcrCapResolver(reader=FakeOcrReader()),
        pass2_phase_b_max_concurrency=1,
    ).run(sample_job_request)

    assert BlockedResolver.calls == 1, "the blocked Gemini resolver is only probed once"
    assert all(event.resolved_cap_number == 7 for event in draft.events)
    assert all(event.match_lineup_id is not None for event in draft.events)
    assert any("local OCR fallback" in w for w in draft.diagnostics.warnings)


def test_concurrent_breaker_trip_probes_gemini_at_most_once_per_worker(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    workers = 2
    barrier = threading.Barrier(workers, timeout=10)
    call_lock = threading.Lock()

    class BlockedResolver(BaseCapResolver):
        calls = 0

        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            with call_lock:
                BlockedResolver.calls += 1
                first_wave = BlockedResolver.calls <= workers
            if first_wave:
                barrier.wait()  # both workers are in flight before either sees the breaker
            raise GeminiCallBlockedError("pass2", BreakerReason.QUOTA_EXHAUSTED, "daily quota")

    class FakeOcrReader:
        @staticmethod
        def readtext(image, allowlist: str = ""):
            return [([], "7", 0.98)]

    draft = _orchestrator_with(
        tmp_path,
        cap_resolver=BlockedResolver(),
        fallback_cap_resolver=LocalOcrCapResolver(reader=FakeOcrReader()),
        pass2_phase_b_max_concurrency=workers,
    ).run(sample_job_request)

    assert BlockedResolver.calls == workers, "only the in-flight probes reached Gemini"
    assert all(event.resolved_cap_number == 7 for event in draft.events)
    assert any("local OCR fallback" in w for w in draft.diagnostics.warnings)


def test_all_six_pass2_failure_reasons_are_emitted_in_their_scenarios(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    def reasons_for(**kwargs) -> list[list[str]]:
        draft = _orchestrator_with(tmp_path, pass2_phase_b_max_concurrency=1, **kwargs).run(
            sample_job_request
        )
        return [event.review_reasons for event in draft.events]

    class NoDetections(MockDetector):
        def detect(self, frame_bgr):
            return []

    class NoCropSelector:
        def select_and_build_sheet(self, **kwargs):
            return None

    class RaisingResolver(BaseCapResolver):
        def __init__(self, error: Exception) -> None:
            self._error = error

        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            raise self._error

    no_frames = reasons_for(ffmpeg=_CountingFFmpeg(frames_enabled=False))
    assert all("NO_FRAMES_EXTRACTED" in r for r in no_frames)
    assert all("NO_PLAYER_TRACKLET_MATCHED" not in r for r in no_frames)

    no_tracklet = reasons_for(detector=NoDetections())
    assert all("NO_PLAYER_TRACKLET_MATCHED" in r for r in no_tracklet)

    no_crops = reasons_for(crop_selector=NoCropSelector())
    assert all("NO_USABLE_CAP_CROPS" in r for r in no_crops)
    assert all("NO_PLAYER_TRACKLET_MATCHED" not in r for r in no_crops)

    api_error = reasons_for(cap_resolver=RaisingResolver(RuntimeError("HTTP 500 boom")))
    assert all("CAP_RESOLUTION_API_ERROR" in r for r in api_error)

    quota = reasons_for(
        cap_resolver=RaisingResolver(
            GeminiCallBlockedError("pass2", BreakerReason.QUOTA_EXHAUSTED, "PerDay")
        )
    )
    assert all("CAP_RESOLUTION_QUOTA_EXHAUSTED" in r for r in quota)

    budget = reasons_for(
        cap_resolver=RaisingResolver(
            GeminiCallBlockedError("pass2", BreakerReason.BUDGET_EXCEEDED, "120 calls")
        )
    )
    assert all("CAP_RESOLUTION_BUDGET_EXCEEDED" in r for r in budget)

    breaker_api = reasons_for(
        cap_resolver=RaisingResolver(
            GeminiCallBlockedError("pass2", BreakerReason.API_ERROR, "HTTP 403")
        )
    )
    assert all("CAP_RESOLUTION_API_ERROR" in r for r in breaker_api)


def test_downscaled_detection_boxes_are_rescaled_to_native_for_actor_matching(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    native_w, native_h, det_w, det_h = 1280, 720, 640, 360
    native = np.zeros((native_h, native_w, 3), dtype=np.uint8)
    cv2.putText(native, "7", (630, 310), cv2.FONT_HERSHEY_SIMPLEX, 3.0, (255, 255, 255), 6)
    ok, encoded = cv2.imencode(".jpg", native)
    assert ok
    seen_shapes: list[tuple[int, ...]] = []

    class DownscalingFFmpeg(MockFFmpegClient):
        def extract_window_frames(self, video_path, center_sec, **kwargs):
            return [
                ExtractedFrame(
                    index=i,
                    video_time_sec=center_sec + 0.1 * i,
                    image_bgr=cv2.resize(native, (det_w, det_h)),
                    native_width=native_w,
                    native_height=native_h,
                    native_jpeg=encoded.tobytes(),
                )
                for i in range(3)
            ]

    class DetectionScaleDetector(MockDetector):
        def detect(self, frame_bgr):
            seen_shapes.append(frame_bgr.shape)
            return super().detect(frame_bgr)  # boxes in detection-image pixels

    crop_sizes: list[tuple[int, ...]] = []

    class SpyResolver(_AlwaysSevenCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            crop_sizes.append(selected_crops.crops[0].crop_bgr.shape)
            return super().resolve(selected_crops, allowed_cap_numbers, cap_color_hint)

    draft = _orchestrator_with(
        tmp_path,
        ffmpeg=DownscalingFFmpeg(),
        detector=DetectionScaleDetector(),
        cap_resolver=SpyResolver(),
    ).run(sample_job_request)

    assert all(shape == (det_h, det_w, 3) for shape in seen_shapes)
    assert all(event.resolved_cap_number == 7 for event in draft.events)
    # Crops come from native pixels: a 0.1*1280 wide player box (plus padding) is > 128 px wide.
    assert all(shape[1] > 128 for shape in crop_sizes)


def test_pass2_extraction_requests_streaming_args_and_no_work_dir(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    ffmpeg = _CountingFFmpeg()
    work_root = tmp_path / "work"
    work_root.mkdir()
    _orchestrator_with(tmp_path, ffmpeg=ffmpeg, work_dir=str(work_root)).run(sample_job_request)

    options = sample_job_request.analysis_options
    assert options.pass2_window_half_sec == 2.5
    assert options.pass2_sample_fps == 4.0
    first = ffmpeg.extract_calls[0]
    assert "work_dir" not in first
    assert first["frame_width"] == 3840
    assert first["frame_height"] == 2160
    assert first["detector_input_width"] == 1280
    assert first["half_window_sec"] == 2.5
    assert first["sample_fps"] == 4.0
    assert list(work_root.rglob("pass2_win_*")) == []


def test_sub_stage_timers_are_added_and_existing_keys_are_preserved(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    draft = _orchestrator_with(tmp_path).run(sample_job_request)
    timings = draft.diagnostics.timings_sec

    legacy_keys = {
        "probe",
        "pass1TranscodeAndChunk",
        "pass1Vlm",
        "pass2ExtractAndTrack",
        "pass2CapResolution",
        "total",
    }
    new_keys = {
        "pass1Mezzanine",
        "pass1ChunkSlicing",
        "pass2FrameExtraction",
        "pass2DetectorInference",
        "pass2Tracking",
        "pass2CropSelection",
        "pass2CapOcr",
    }
    assert legacy_keys <= set(timings)
    assert new_keys <= set(timings)
    assert all(timings[key] >= 0.0 for key in new_keys)

    skipped = _orchestrator_with(tmp_path).run(
        sample_job_request.model_copy(
            update={
                "analysis_options": sample_job_request.analysis_options.model_copy(
                    update={"skip_pass2": True}
                )
            }
        )
    )
    assert (legacy_keys | new_keys) <= set(skipped.diagnostics.timings_sec)
    assert skipped.diagnostics.timings_sec["pass2FrameExtraction"] == 0.0


def test_model_metadata_reports_all_three_slots_and_verifier_stays_unused(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    draft = _orchestrator_with(tmp_path).run(sample_job_request)
    metadata = draft.model_metadata
    assert metadata.pass1_vlm_model == "gemini-4-hydrogen"
    assert metadata.pass2_cap_model == "gemini-3.5-flash-lite"
    assert metadata.verifier_model == "gemini-4-hydrogen"
    usage = draft.diagnostics.gemini_usage
    assert usage.verifier.model == "gemini-4-hydrogen"
    assert usage.verifier.usage.call_count == 0
    assert usage.pass2.model == "gemini-3.5-flash-lite"


def test_gemini_job_scope_budget_is_applied_per_job_through_worker_threads(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    """The per-job breaker/budget scope must be visible inside Phase B worker threads."""
    resilience = GeminiResilience("pass2", "gemini-3.5-flash-lite", max_retries=0)

    class BudgetedResolver(BaseCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            resilience.call(lambda: SimpleNamespace(usage_metadata=None), paced=False)
            return CapResolutionResult(
                resolved_cap_number=7,
                confidence=0.95,
                candidate_scores={7: 0.95},
                reasoning="ok",
            )

    draft = _orchestrator_with(
        tmp_path,
        cap_resolver=BudgetedResolver(),
        gemini_max_calls_per_job=2,
        pass2_phase_b_max_concurrency=3,
    ).run(sample_job_request)

    resolved = [e for e in draft.events if e.resolved_cap_number == 7]
    blocked = [e for e in draft.events if "CAP_RESOLUTION_BUDGET_EXCEEDED" in e.review_reasons]
    assert len(resolved) == 2
    assert len(blocked) == len(_EVENT_TIMES_SEC) - 2
    assert any("budget" in w and "circuit breaker" in w for w in draft.diagnostics.warnings)

    # A second job starts with a fresh budget instead of inheriting the exhausted one.
    second = _orchestrator_with(
        tmp_path,
        cap_resolver=BudgetedResolver(),
        gemini_max_calls_per_job=2,
        pass2_phase_b_max_concurrency=1,
    ).run(sample_job_request)
    assert len([e for e in second.events if e.resolved_cap_number == 7]) == 2


def test_extraction_semaphore_caps_4k_decoders_while_phase_a_overlaps_detector_io(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    """Phase A concurrency (network I/O overlap) must not multiply simultaneous 4K decoders."""
    ffmpeg = _CountingFFmpeg(delay_sec=0.03)
    detector = _ConcurrencyProbeDetector(concurrent=True, delay_sec=0.03)
    draft = _orchestrator_with(
        tmp_path,
        ffmpeg=ffmpeg,
        detector=detector,
        pass2_phase_a_max_concurrency=6,
        ffmpeg_extraction_max_concurrency=2,
        detector_max_concurrency=4,
    ).run(sample_job_request)

    assert len(draft.events) == len(_EVENT_TIMES_SEC)
    assert ffmpeg.max_active == 2, "at most 2 simultaneous 4K extractions"
    assert detector.max_active > 1, "...while detector calls from other tasks still overlap"

    serial = _CountingFFmpeg(delay_sec=0.02)
    _orchestrator_with(
        tmp_path,
        ffmpeg=serial,
        pass2_phase_a_max_concurrency=6,
        ffmpeg_extraction_max_concurrency=1,
    ).run(sample_job_request)
    assert serial.max_active == 1


def test_extraction_slot_wait_is_cancellable_and_released_on_failure(
    monkeypatch: pytest.MonkeyPatch,
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    release = threading.Event()
    entered = threading.Event()
    cancellation_observed = threading.Event()

    class BlockingFFmpeg(MockFFmpegClient):
        def extract_window_frames(self, video_path, center_sec, **kwargs):
            entered.set()
            release.wait(timeout=5.0)
            raise RuntimeError("decoder exploded")

    cancel_event = threading.Event()
    orchestrator = _orchestrator_with(
        tmp_path,
        ffmpeg=BlockingFFmpeg(),
        pass2_phase_a_max_concurrency=2,
        ffmpeg_extraction_max_concurrency=1,
    )
    outcome: dict[str, BaseException] = {}
    original_check = orchestrator._check_cancelled

    def recording_check(event: threading.Event | None) -> None:
        try:
            original_check(event)
        except JobCancelledError:
            cancellation_observed.set()
            raise

    monkeypatch.setattr(orchestrator, "_check_cancelled", recording_check)

    def run_job() -> None:
        try:
            orchestrator.run(sample_job_request, cancel_event=cancel_event)
        except BaseException as exc:  # noqa: BLE001 - captured for assertions
            outcome["error"] = exc

    worker = threading.Thread(target=run_job)
    worker.start()
    assert entered.wait(timeout=5.0)
    time.sleep(0.2)  # the second Phase A task is now waiting for the only extraction slot
    cancel_event.set()
    # The first decoder keeps the only slot until the waiting task has observed cancellation.
    assert cancellation_observed.wait(timeout=5.0), "the slot wait must notice cancellation"
    release.set()
    worker.join(timeout=10.0)
    assert not worker.is_alive(), "waiting for a slot must not hang past cancellation"
    # The earliest failure by item order (the decoder) is the one reported.
    assert isinstance(outcome["error"], RuntimeError)
    assert "decoder exploded" in str(outcome["error"])

    # The slot was released by the failing task (no leak): a fresh job can acquire it.
    assert orchestrator._extraction_slots.acquire(timeout=1.0)
    orchestrator._extraction_slots.release()


def test_phase_a_releases_frame_buffers_and_prepared_items_hold_only_small_results(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    """Frames (detection images + native JPEG copies) must not outlive their Phase A task."""
    import gc
    import weakref

    frame_refs: list[weakref.ReferenceType] = []

    class RecordingFFmpeg(MockFFmpegClient):
        def extract_window_frames(self, video_path, center_sec, **kwargs):
            frames = super().extract_window_frames(video_path, center_sec, **kwargs)
            frame_refs.extend(weakref.ref(frame.image_bgr) for frame in frames)
            return frames

    orchestrator = _orchestrator_with(tmp_path, ffmpeg=RecordingFFmpeg())
    draft = orchestrator.run(sample_job_request)
    assert len(draft.events) == len(_EVENT_TIMES_SEC)

    # Phase B has already finished here; if prepared items kept frames alive, they would
    # accumulate across all events until the end of the job.
    gc.collect()
    assert frame_refs
    assert all(ref() is None for ref in frame_refs)


def test_prepared_events_do_not_retain_frames_between_phase_a_and_phase_b(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    import gc
    import weakref

    frame_refs: list[weakref.ReferenceType] = []

    class RecordingFFmpeg(MockFFmpegClient):
        def extract_window_frames(self, video_path, center_sec, **kwargs):
            frames = super().extract_window_frames(video_path, center_sec, **kwargs)
            frame_refs.extend(weakref.ref(frame.image_bgr) for frame in frames)
            return frames

    leaked_at_phase_b: list[int] = []

    class InspectingResolver(_AlwaysSevenCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None):
            gc.collect()
            leaked_at_phase_b.append(sum(ref() is not None for ref in frame_refs))
            return super().resolve(selected_crops, allowed_cap_numbers, cap_color_hint)

    _orchestrator_with(
        tmp_path,
        ffmpeg=RecordingFFmpeg(),
        cap_resolver=InspectingResolver(),
        pass2_phase_b_max_concurrency=1,
    ).run(sample_job_request)

    assert leaked_at_phase_b
    assert all(count == 0 for count in leaked_at_phase_b)
