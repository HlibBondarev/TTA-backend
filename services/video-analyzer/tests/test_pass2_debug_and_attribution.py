"""End-to-end tests for opponent suppression, Pass 2 debug artifacts and attribution counters."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np

from tests.test_orchestrator_and_api import (
    MockDetector,
    _AlwaysSevenCapResolver,
    _CountingFFmpeg,
    _orchestrator_with,
)
from video_analyzer.detectors.base import CanonicalRole, Detection
from video_analyzer.pass2.cap_resolver import CapResolutionResult
from video_analyzer.pass2.debug_artifacts import SUMMARY_FILENAME
from video_analyzer.pass2.failure_reasons import (
    REVIEW_ACTOR_TEAM_UNVERIFIED,
    REVIEW_OCR_CAP_COLOR_MISMATCH,
    Pass2FailureReason,
)
from video_analyzer.schemas.draft import PipelineDiagnostics
from video_analyzer.schemas.request import AnalyzeJobRequest

EVENT_COUNT = 6
ATTRIBUTION_WARNING = "Pass 2 actor attribution"


class _LabelDetector(MockDetector):
    """Mock detector returning the standard player box under a configurable label."""

    def __init__(self, label: str) -> None:
        self._label = label

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        h, w = frame_bgr.shape[:2]
        return [
            Detection(
                bbox_xyxy=(0.45 * w, 0.35 * h, 0.55 * w, 0.65 * h),
                confidence=0.91,
                role=CanonicalRole.PLAYER,
                raw_class_name=self._label,
            )
        ]


class _NoDetector(MockDetector):
    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        return []


class _ObservedColorResolver(_AlwaysSevenCapResolver):
    def __init__(
        self, observed_cap_color: str | None, tile_cap_colors: tuple[str | None, ...] = ()
    ) -> None:
        super().__init__()
        self._observed = observed_cap_color
        self._tiles = tile_cap_colors

    def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None, **_hints):
        return CapResolutionResult(
            resolved_cap_number=7,
            confidence=0.95,
            candidate_scores={7: 0.95},
            reasoning="Cap #7",
            observed_cap_color=self._observed,
            tile_cap_colors=self._tiles,
            raw_response={"observed_cap_color": self._observed},
        )


def _run(request: AnalyzeJobRequest, tmp_path: Path, debug: bool = True, **orchestrator_kwargs):
    debug_dir = tmp_path / "debug" if debug else None
    orchestrator = _orchestrator_with(tmp_path, **orchestrator_kwargs)
    return orchestrator.run(request, debug_dir=debug_dir), tmp_path / "debug"


def _event_json(debug_dir: Path, index: int) -> dict:
    return json.loads((debug_dir / f"event_{index:03d}_pass2.json").read_text(encoding="utf-8"))


def _attribution_warnings(draft) -> list[str]:
    return [w for w in draft.diagnostics.warnings if ATTRIBUTION_WARNING in w]


# ------------------------------------------------------------------- matched events


def test_matched_events_write_every_artifact_and_the_summary(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, debug_dir = _run(sample_job_request, tmp_path, detector=_LabelDetector("White team"))

    assert len(draft.events) == EVENT_COUNT
    for index in range(1, EVENT_COUNT + 1):
        for suffix in ("all_detections.jpg", "crops.jpg", "keyframe.jpg", "pass2.json"):
            assert (debug_dir / f"event_{index:03d}_{suffix}").is_file()
    payload = _event_json(debug_dir, 1)
    assert payload["matchedTracklet"]["teamClass"] == "tracked"
    assert payload["candidates"][0]["team"]["labelVotes"] == {"tracked": 3}
    assert payload["candidates"][0]["exclusionReason"] is None
    assert payload["cropObservations"]
    assert payload["ocr"]["resolvedCapNumber"] == 7
    assert payload["gates"] == {
        "WEAK_ACTOR_MATCH": False,
        "ACTOR_TEAM_UNVERIFIED": False,
        "OPPONENT_GOALKEEPER_SIDE": False,
    }
    summary = json.loads((debug_dir / SUMMARY_FILENAME).read_text(encoding="utf-8"))
    assert summary["events"] == EVENT_COUNT
    assert summary["opponentTrackletExclusions"] == 0
    assert all(event.match_lineup_id is not None for event in draft.events)
    assert _attribution_warnings(draft) == []


def test_no_debug_dir_writes_nothing_and_changes_no_results(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, debug_dir = _run(
        sample_job_request, tmp_path, debug=False, detector=_LabelDetector("White team")
    )

    assert not debug_dir.exists()
    assert all(event.match_lineup_id is not None for event in draft.events)


# ----------------------------------------------------------- failed and unmatched events


def test_unmatched_events_still_write_detection_overlay_and_json(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, debug_dir = _run(sample_job_request, tmp_path, detector=_NoDetector())

    for index in range(1, EVENT_COUNT + 1):
        assert (debug_dir / f"event_{index:03d}_all_detections.jpg").is_file()
        assert not (debug_dir / f"event_{index:03d}_crops.jpg").exists()
        payload = _event_json(debug_dir, index)
        assert payload["failureReason"] == Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED
        assert payload["matchedTracklet"] is None
        assert payload["ocr"] is None
    assert all("NO_PLAYER_TRACKLET_MATCHED" in e.review_reasons for e in draft.events)


def test_events_without_frames_still_write_the_json_breakdown(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    _, debug_dir = _run(sample_job_request, tmp_path, ffmpeg=_CountingFFmpeg(frames_enabled=False))

    payload = _event_json(debug_dir, 1)
    assert payload["failureReason"] == Pass2FailureReason.NO_FRAMES_EXTRACTED
    assert not (debug_dir / "event_001_all_detections.jpg").exists()


# --------------------------------------------------------------- opponent suppression


def test_opponent_only_tracklets_are_never_attributed(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, debug_dir = _run(sample_job_request, tmp_path, detector=_LabelDetector("Blue team"))

    for event in draft.events:
        assert event.match_lineup_id is None
        assert event.resolved_cap_number is None
        assert event.needs_review is True
        assert Pass2FailureReason.NO_TRACKED_TEAM_ACTOR in event.review_reasons
    payload = _event_json(debug_dir, 1)
    assert payload["failureReason"] == Pass2FailureReason.NO_TRACKED_TEAM_ACTOR
    assert payload["candidates"][0]["exclusionReason"] == "OPPONENT_TEAM"
    assert (debug_dir / "event_001_all_detections.jpg").is_file()
    warnings = _attribution_warnings(draft)
    assert len(warnings) == 1
    assert f"{EVENT_COUNT} opponent tracklet exclusion(s)" in warnings[0]
    summary = json.loads((debug_dir / SUMMARY_FILENAME).read_text(encoding="utf-8"))
    assert summary["opponentTrackletExclusions"] == EVENT_COUNT


def test_unlabeled_players_are_kept_but_never_auto_confirmed(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, _ = _run(sample_job_request, tmp_path, debug=False, detector=_LabelDetector("Player"))

    for event in draft.events:
        assert event.match_lineup_id is None
        assert event.needs_review is True
        assert REVIEW_ACTOR_TEAM_UNVERIFIED in event.review_reasons
        assert [c.cap_number for c in event.candidate_lineup_ids] == [7]


# ------------------------------------------------------------------- OCR color rejection


def test_ocr_observed_color_mismatch_blocks_events_and_is_counted(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, debug_dir = _run(
        sample_job_request,
        tmp_path,
        detector=_LabelDetector("White team"),
        cap_resolver=_ObservedColorResolver("blue"),
    )

    for event in draft.events:
        assert event.match_lineup_id is None
        assert event.resolved_cap_number is None
        assert event.needs_review is True
        assert REVIEW_OCR_CAP_COLOR_MISMATCH in event.review_reasons
    payload = _event_json(debug_dir, 1)
    assert payload["ocr"]["observedCapColor"] == "blue"
    assert payload["ocr"]["expectedCapColor"] == "white"
    assert payload["ocr"]["opponentCapColor"] == "blue"
    assert payload["ocr"]["rawResponse"] == {"observed_cap_color": "blue"}
    assert payload["gates"][REVIEW_OCR_CAP_COLOR_MISMATCH] is True
    warnings = _attribution_warnings(draft)
    assert len(warnings) == 1
    assert f"{EVENT_COUNT} OCR cap-color rejection(s)" in warnings[0]
    summary = json.loads((debug_dir / SUMMARY_FILENAME).read_text(encoding="utf-8"))
    assert summary["ocrCapColorRejections"] == EVENT_COUNT


def test_ocr_observed_color_matching_the_expected_color_is_accepted(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, _ = _run(
        sample_job_request,
        tmp_path,
        debug=False,
        detector=_LabelDetector("White team"),
        cap_resolver=_ObservedColorResolver("White"),
    )

    assert all(event.match_lineup_id is not None for event in draft.events)
    assert _attribution_warnings(draft) == []


def test_pipeline_diagnostics_schema_is_unchanged() -> None:
    assert set(PipelineDiagnostics.model_fields) == {
        "pass1_chunks_processed",
        "pass1_raw_candidate_count",
        "deduplicated_event_count",
        "pass2_windows_processed",
        "resolved_actor_count",
        "unresolved_actor_count",
        "warnings",
        "timings_sec",
        "gemini_usage",
    }


def test_resolver_receives_goalkeeper_and_opponent_cap_colors(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    received: list[dict] = []

    class RecordingResolver(_AlwaysSevenCapResolver):
        def resolve(self, selected_crops, allowed_cap_numbers, cap_color_hint=None, **hints):
            received.append({"cap_color_hint": cap_color_hint, **hints})
            return super().resolve(selected_crops, allowed_cap_numbers, cap_color_hint, **hints)

    _run(
        sample_job_request,
        tmp_path,
        debug=False,
        detector=_LabelDetector("White team"),
        cap_resolver=RecordingResolver(),
    )

    assert received
    assert all(call["goalkeeper_cap_color"] == "red" for call in received)
    assert all(call["opponent_cap_color"] == "blue" for call in received)
    assert all(call["cap_color_hint"] == "white" for call in received)


def test_mixed_tile_colors_without_an_overall_color_block_events_and_are_counted(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, debug_dir = _run(
        sample_job_request,
        tmp_path,
        detector=_LabelDetector("White team"),
        cap_resolver=_ObservedColorResolver(None, ("white", "blue", "blue", "white")),
    )

    for event in draft.events:
        assert event.match_lineup_id is None
        assert event.needs_review is True
        assert REVIEW_OCR_CAP_COLOR_MISMATCH in event.review_reasons
    payload = _event_json(debug_dir, 1)
    assert payload["ocr"]["tileCapColors"] == ["white", "blue", "blue", "white"]
    assert payload["gates"][REVIEW_OCR_CAP_COLOR_MISMATCH] is True
    summary = json.loads((debug_dir / SUMMARY_FILENAME).read_text(encoding="utf-8"))
    assert summary["ocrCapColorRejections"] == EVENT_COUNT


def test_clean_white_sheet_with_one_noisy_tile_still_auto_confirms(
    sample_job_request: AnalyzeJobRequest, tmp_path: Path
) -> None:
    draft, _ = _run(
        sample_job_request,
        tmp_path,
        debug=False,
        detector=_LabelDetector("White team"),
        cap_resolver=_ObservedColorResolver("white", ("white", "white", "blue", "white")),
    )

    assert all(event.match_lineup_id is not None for event in draft.events)
    assert _attribution_warnings(draft) == []
