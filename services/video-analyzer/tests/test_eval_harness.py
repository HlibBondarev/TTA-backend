"""Offline evaluation harness tests: schema, scoring metrics, and the ``evaluate`` CLI."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from uuid import UUID

import pytest
from pydantic import ValidationError

from video_analyzer.cli import main as cli_main
from video_analyzer.eval.schema import EvalDraft, GroundTruthLabels
from video_analyzer.eval.scoring import evaluate_draft, format_report, match_by_time

SERVICE_ROOT = Path(__file__).resolve().parents[1]
FIXTURES = Path(__file__).resolve().parent / "fixtures" / "eval"
DRAFT_FIXTURE = FIXTURES / "synthetic_draft.json"
LABELS_FIXTURE = FIXTURES / "synthetic_labels.json"
GOAL_ID = UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f57")
GOAL_CONCEDED_ID = UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f72")
SAMPLE_LABELS = SERVICE_ROOT / "eval_labels" / "sample_labels.example.json"


def _load_fixtures() -> tuple[EvalDraft, GroundTruthLabels]:
    draft = EvalDraft.model_validate_json(DRAFT_FIXTURE.read_text(encoding="utf-8"))
    labels = GroundTruthLabels.model_validate_json(LABELS_FIXTURE.read_text(encoding="utf-8"))
    return draft, labels


def test_synthetic_fixtures_load_and_ignore_unknown_draft_fields() -> None:
    draft, labels = _load_fixtures()
    assert len(draft.events) == 4
    assert len(draft.time_anchors) == 2
    assert draft.events[0].resolved_cap_number == 7
    assert draft.events[1].resolved_cap_number is None
    assert len(labels.events) == 4
    assert len(labels.anchors) == 3
    assert labels.events[2].cap_number is None
    assert labels.anchors[0].type == "PeriodStart"


def test_scoring_reports_precision_recall_label_anchor_and_cap_accuracy() -> None:
    draft, labels = _load_fixtures()
    report = evaluate_draft(draft, labels, tolerance_sec=3.0)

    # Matches: 11.5~10.0, 41.0~40.0, 75.5~75.0. The 300s prediction and 120s label are unmatched.
    assert (report.predicted_events, report.labeled_events, report.matched_events) == (4, 4, 3)
    assert report.event_precision == pytest.approx(0.75)
    assert report.event_recall == pytest.approx(0.75)
    # Of the 3 matched pairs only the first two have the right eventDefinitionId.
    assert (report.label_correct, report.label_accuracy) == (2, pytest.approx(0.6667, abs=1e-4))
    # Only the PeriodStart anchor is within tolerance (PeriodEnd is 10s off, stoppage missing).
    assert (report.labeled_anchors, report.matched_anchors) == (3, 1)
    assert report.anchor_accuracy == pytest.approx(0.3333, abs=1e-4)
    # Cap labels exist on matched pairs 1 and 2; only pair 1 was resolved correctly.
    assert (report.cap_labeled_matches, report.cap_correct) == (2, 1)
    assert report.cap_resolution_accuracy == pytest.approx(0.5)


def test_tolerance_changes_what_counts_as_a_match() -> None:
    draft, labels = _load_fixtures()

    strict = evaluate_draft(draft, labels, tolerance_sec=0.5)
    assert strict.matched_events == 1  # only 75.5 vs 75.0
    assert strict.matched_anchors == 0

    loose = evaluate_draft(draft, labels, tolerance_sec=10.0)
    assert loose.matched_anchors == 2  # PeriodEnd is exactly 10s away
    assert loose.matched_events == 3

    for invalid in (-1.0, float("nan"), float("inf"), float("-inf")):
        with pytest.raises(ValueError, match="finite, non-negative"):
            evaluate_draft(draft, labels, tolerance_sec=invalid)


def test_matching_is_one_to_one_closest_first_and_deterministic() -> None:
    # Two predictions compete for one label; the closer one wins, the other is a false positive.
    pairs = match_by_time([9.0, 10.2], [10.0], tolerance_sec=3.0)
    assert pairs == [(1, 0)]

    # Equal distances tie-break on the lower prediction index, then lower label index.
    assert match_by_time([8.0, 12.0], [10.0], tolerance_sec=3.0) == [(0, 0)]

    # Optimal-by-distance greedy assignment across several items.
    assert match_by_time([1.0, 5.0, 9.0], [5.5, 9.4, 100.0], tolerance_sec=2.0) == [(1, 0), (2, 1)]


def test_anchor_matching_requires_same_type_and_period() -> None:
    labels = GroundTruthLabels.model_validate(
        {
            "clipId": "c",
            "anchors": [
                {"timeSec": 10.0, "type": "PeriodStart", "periodNumber": 1},
                {"timeSec": 50.0, "type": "StoppageStart"},
            ],
        }
    )
    draft = EvalDraft.model_validate(
        {
            "timeAnchors": [
                {"type": "PeriodEnd", "periodNumber": 1, "videoTimeSec": 10.0},  # wrong type
                {"type": "PeriodStart", "periodNumber": 2, "videoTimeSec": 10.0},  # wrong period
                {
                    "type": "StoppageStart",
                    "periodNumber": 3,
                    "videoTimeSec": 51.0,
                },  # no label period
            ]
        }
    )
    report = evaluate_draft(draft, labels)
    assert report.matched_anchors == 1
    assert report.anchor_accuracy == pytest.approx(0.5)


def test_empty_inputs_report_none_instead_of_dividing_by_zero() -> None:
    empty_draft = EvalDraft()
    empty_labels = GroundTruthLabels(clip_id="empty")
    report = evaluate_draft(empty_draft, empty_labels)
    assert report.event_precision is None
    assert report.event_recall is None
    assert report.label_accuracy is None
    assert report.anchor_accuracy is None
    assert report.cap_resolution_accuracy is None
    assert "n/a" in format_report(report)

    draft, _ = _load_fixtures()
    no_labels = evaluate_draft(draft, empty_labels)
    assert no_labels.event_precision == 0.0
    assert no_labels.event_recall is None
    assert no_labels.to_dict()["predicted_events"] == 4


def test_label_schema_validation_rejects_malformed_labels() -> None:
    with pytest.raises(ValidationError):
        GroundTruthLabels.model_validate({"events": []})  # clipId is required
    with pytest.raises(ValidationError):
        GroundTruthLabels.model_validate(
            {"clipId": "c", "events": [{"timeSec": -1.0, "eventDefinitionId": str(GOAL_ID)}]}
        )
    with pytest.raises(ValidationError):
        GroundTruthLabels.model_validate(
            {"clipId": "c", "anchors": [{"timeSec": 1.0, "type": "NotAnAnchorType"}]}
        )
    with pytest.raises(ValidationError):
        GroundTruthLabels.model_validate(
            {"clipId": "c", "events": [{"timeSec": 1.0, "eventDefinitionId": "not-a-uuid"}]}
        )


def test_sample_labels_template_is_valid_documented_and_the_only_committed_labels() -> None:
    labels = GroundTruthLabels.model_validate_json(SAMPLE_LABELS.read_text(encoding="utf-8"))
    assert "TEMPLATE ONLY" in (labels.description or "")
    assert "REPLACE-ME" in labels.clip_id
    assert labels.events
    assert labels.anchors

    # Only git-tracked files count: git-ignored local Clip 1 / Clip 2 labels must not fail this.
    try:
        listing = subprocess.run(
            ["git", "ls-files", "--", "eval_labels"],
            cwd=SERVICE_ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout
    except (OSError, subprocess.CalledProcessError):
        pytest.skip("git metadata is not available to list tracked label files")
    tracked = sorted(Path(line).name for line in listing.splitlines() if line.strip())
    assert tracked == ["sample_labels.example.json"], "no real Clip 1 / Clip 2 labels in the repo"
    ignore_rules = (SERVICE_ROOT / ".gitignore").read_text(encoding="utf-8")
    assert "eval_labels/*" in ignore_rules
    assert "!eval_labels/sample_labels.example.json" in ignore_rules


def test_evaluate_cli_runs_fully_offline_without_settings_or_api_keys(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    tmp_path: Path,
) -> None:
    def forbidden(*args, **kwargs):
        raise AssertionError("evaluate must not load Settings or build the pipeline")

    monkeypatch.setattr("video_analyzer.cli.Settings", forbidden)
    monkeypatch.setattr("video_analyzer.cli.build_default_orchestrator", forbidden)
    monkeypatch.chdir(tmp_path)  # no .env present, no network configured

    exit_code = cli_main(
        ["evaluate", "--draft", str(DRAFT_FIXTURE), "--labels", str(LABELS_FIXTURE)]
    )
    assert exit_code == 0
    stdout = capsys.readouterr().out
    assert "synthetic-fixture" in stdout
    assert "tolerance +/-3s" in stdout
    assert "Event precision:" in stdout
    assert "75.0% (3/4)" in stdout
    assert "Event recall:" in stdout
    assert "Label accuracy:          66.7% (2/3)" in stdout
    assert "Anchor accuracy:         33.3% (1/3)" in stdout
    assert "Cap resolution accuracy: 50.0% (1/2)" in stdout

    exit_code = cli_main(
        [
            "evaluate",
            "--draft",
            str(DRAFT_FIXTURE),
            "--labels",
            str(LABELS_FIXTURE),
            "--tolerance-sec",
            "0.5",
        ]
    )
    assert exit_code == 0
    assert "tolerance +/-0.5s" in capsys.readouterr().out


def test_evaluate_cli_reports_missing_and_invalid_files_with_exit_code_2(
    capsys: pytest.CaptureFixture[str],
    tmp_path: Path,
) -> None:
    missing = tmp_path / "missing.json"
    assert cli_main(["evaluate", "--draft", str(missing), "--labels", str(LABELS_FIXTURE)]) == 2
    assert "draft file not found" in capsys.readouterr().err

    assert cli_main(["evaluate", "--draft", str(DRAFT_FIXTURE), "--labels", str(missing)]) == 2
    assert "labels file not found" in capsys.readouterr().err

    bad_labels = tmp_path / "bad_labels.json"
    bad_labels.write_text(json.dumps({"events": []}), encoding="utf-8")
    assert cli_main(["evaluate", "--draft", str(DRAFT_FIXTURE), "--labels", str(bad_labels)]) == 2
    assert "invalid draft or labels" in capsys.readouterr().err

    args = ["evaluate", "--draft", str(DRAFT_FIXTURE), "--labels", str(LABELS_FIXTURE)]
    for invalid in ("-2", "nan", "inf"):
        assert cli_main([*args, "--tolerance-sec", invalid]) == 2
        assert "finite, non-negative" in capsys.readouterr().err


def test_evaluate_scores_a_real_draft_roundtrip(
    sample_job_request,
    tmp_path: Path,
) -> None:
    """A draft serialized by the pipeline is consumable by the lenient eval view."""
    from tests.test_orchestrator_and_api import _build_mock_orchestrator

    draft = _build_mock_orchestrator(work_dir=tmp_path).run(sample_job_request)
    draft_path = tmp_path / "draft.json"
    draft_path.write_text(
        json.dumps(draft.model_dump(mode="json", by_alias=True)), encoding="utf-8"
    )
    labels = GroundTruthLabels.model_validate(
        {
            "clipId": "roundtrip",
            "events": [
                {"timeSec": 45.0, "eventDefinitionId": str(GOAL_ID), "capNumber": 7},
                {"timeSec": 110.0, "eventDefinitionId": str(GOAL_CONCEDED_ID)},
            ],
            "anchors": [{"timeSec": 5.0, "type": "PeriodStart", "periodNumber": 1}],
        }
    )
    report = evaluate_draft(EvalDraft.model_validate_json(draft_path.read_text("utf-8")), labels)
    assert report.event_precision == 1.0
    assert report.event_recall == 1.0
    assert report.label_accuracy == 1.0
    assert report.cap_resolution_accuracy == 1.0
    assert report.anchor_accuracy == 1.0


def _single_event_pair(
    draft_id: UUID, label_id: UUID, draft_prompt_code: str = "EVT_01_G_POS"
) -> tuple[EvalDraft, GroundTruthLabels]:
    draft = EvalDraft.model_validate(
        {
            "events": [
                {
                    "videoTimeSec": 10.0,
                    "eventDefinitionId": str(draft_id),
                    "promptCode": draft_prompt_code,
                }
            ]
        }
    )
    labels = GroundTruthLabels.model_validate(
        {"clipId": "c", "events": [{"timeSec": 10.0, "eventDefinitionId": str(label_id)}]}
    )
    return draft, labels


def test_label_accuracy_matches_strictly_by_event_definition_id() -> None:
    # Same id but a different (internal) promptCode and no name: still correct.
    draft, labels = _single_event_pair(GOAL_ID, GOAL_ID, draft_prompt_code="EVT_99_ANY_POS")
    assert evaluate_draft(draft, labels).label_correct == 1

    # Different id with an identical promptCode: incorrect (promptCode is never consulted).
    draft, labels = _single_event_pair(GOAL_CONCEDED_ID, GOAL_ID)
    report = evaluate_draft(draft, labels)
    assert (report.matched_events, report.label_correct) == (1, 0)
    assert report.label_accuracy == 0.0


def test_positive_and_negative_goal_are_distinct_by_event_definition_id() -> None:
    # Both definitions are named "Goal"; only their ids tell them apart.
    draft, labels = _single_event_pair(GOAL_CONCEDED_ID, GOAL_ID)
    assert evaluate_draft(draft, labels).label_accuracy == 0.0


def test_eval_schemas_require_a_valid_event_definition_id() -> None:
    with pytest.raises(ValidationError):
        EvalDraft.model_validate({"events": [{"videoTimeSec": 1.0, "promptCode": "EVT_01_G_POS"}]})
    with pytest.raises(ValidationError):
        GroundTruthLabels.model_validate(
            {"clipId": "c", "events": [{"timeSec": 1.0, "promptCode": "EVT_01_G_POS"}]}
        )


def test_sample_labels_template_uses_event_definition_ids() -> None:
    labels = GroundTruthLabels.model_validate_json(SAMPLE_LABELS.read_text(encoding="utf-8"))
    assert {event.event_definition_id for event in labels.events} == {GOAL_ID, GOAL_CONCEDED_ID}
