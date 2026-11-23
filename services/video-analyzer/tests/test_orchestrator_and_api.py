"""End-to-end pipeline, sample snapshot, FastAPI, and CLI contract tests."""

from __future__ import annotations

import json
import time
from pathlib import Path

import cv2
import numpy as np
from fastapi.testclient import TestClient

from video_analyzer.api.app import create_app
from video_analyzer.cli import main as cli_main
from video_analyzer.config import Settings
from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.media.ffmpeg_client import ExtractedFrame, VideoChunk, VideoProbeResult
from video_analyzer.pass1.gemini_client import BasePass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawAnchor, Pass1RawEvent
from video_analyzer.pass2.cap_resolver import BaseCapResolver, CapResolutionResult
from video_analyzer.pipeline.orchestrator import VideoAnalysisOrchestrator
from video_analyzer.schemas.draft import TimeAnchorType
from video_analyzer.schemas.request import AnalyzeJobRequest, MatchSnapshot


class MockFFmpegClient:
    def resolve_source_path(self, source, work_dir: Path) -> Path:
        return Path(source.local_path or "dummy.mp4")

    def probe(self, video_path: Path) -> VideoProbeResult:
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


def _build_mock_orchestrator() -> VideoAnalysisOrchestrator:
    settings = Settings(
        detector_provider="roboflow",
        detector_model_path_or_id="waterpolo-salvasc/5",
        internal_api_key="secret-test-key",
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


def test_orchestrator_end_to_end_draft_contract(sample_job_request: AnalyzeJobRequest) -> None:
    orchestrator = _build_mock_orchestrator()
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
) -> None:
    settings = Settings(internal_api_key="secret-test-key")
    app = create_app(settings=settings, orchestrator=_build_mock_orchestrator())

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
        Path(__file__).resolve().parents[1]
        / "sample_snapshots"
        / "waterpolo_default_snapshot.json"
    )

    monkeypatch.setattr(
        "video_analyzer.cli.build_default_orchestrator",
        lambda settings: _build_mock_orchestrator(),
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
