"""Tests for anchor precision and the auto-confirmed-wrong evaluation metrics."""

from __future__ import annotations

from uuid import UUID

import pytest

from video_analyzer.eval.schema import (
    EvalDraft,
    EvalDraftAnchor,
    EvalDraftEvent,
    GroundTruthLabels,
    LabeledAnchor,
    LabeledEvent,
)
from video_analyzer.eval.scoring import evaluate_draft, format_report

GOAL_ID = UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f57")
STEAL_ID = UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f51")


def _labels(events: list[LabeledEvent], anchors: list[LabeledAnchor] | None = None):
    return GroundTruthLabels(clip_id="clip", events=events, anchors=anchors or [])


def test_anchor_precision_penalizes_extra_predicted_anchors() -> None:
    draft = EvalDraft(
        time_anchors=[
            EvalDraftAnchor(video_time_sec=5.0, type="PeriodStart", period_number=1),
            EvalDraftAnchor(video_time_sec=60.0, type="StoppageStart", period_number=1),
            EvalDraftAnchor(video_time_sec=90.0, type="StoppageEnd", period_number=1),
            EvalDraftAnchor(video_time_sec=200.0, type="StoppageStart", period_number=1),
        ]
    )
    labels = _labels(
        [],
        [
            LabeledAnchor(time_sec=5.0, type="PeriodStart", period_number=1),
            LabeledAnchor(time_sec=61.0, type="StoppageStart", period_number=1),
        ],
    )

    report = evaluate_draft(draft, labels)

    assert report.predicted_anchors == 4
    assert report.matched_anchors == 2
    assert report.anchor_accuracy == pytest.approx(1.0)  # recall is perfect
    assert report.anchor_precision == pytest.approx(0.5)  # but half of the output is spurious


def test_anchor_precision_is_none_without_predicted_anchors() -> None:
    report = evaluate_draft(
        EvalDraft(),
        _labels([], [LabeledAnchor(time_sec=5.0, type="PeriodStart")]),
    )

    assert report.anchor_precision is None
    assert report.anchor_accuracy == 0.0


def test_auto_confirmed_wrong_counts_wrong_label_or_cap_only_when_not_needing_review() -> None:
    draft = EvalDraft(
        events=[
            # Correct label and cap, auto-confirmed.
            EvalDraftEvent(
                video_time_sec=10.0,
                event_definition_id=GOAL_ID,
                resolved_cap_number=7,
                needs_review=False,
            ),
            # Wrong label, auto-confirmed -> counted.
            EvalDraftEvent(
                video_time_sec=30.0,
                event_definition_id=STEAL_ID,
                resolved_cap_number=3,
                needs_review=False,
            ),
            # Right label, wrong cap, auto-confirmed -> counted.
            EvalDraftEvent(
                video_time_sec=50.0,
                event_definition_id=GOAL_ID,
                resolved_cap_number=9,
                needs_review=False,
            ),
            # Wrong label but flagged for review -> not counted.
            EvalDraftEvent(
                video_time_sec=70.0,
                event_definition_id=STEAL_ID,
                resolved_cap_number=None,
                needs_review=True,
            ),
        ]
    )
    labels = _labels(
        [
            LabeledEvent(time_sec=10.0, event_definition_id=GOAL_ID, cap_number=7),
            LabeledEvent(time_sec=30.0, event_definition_id=GOAL_ID, cap_number=3),
            LabeledEvent(time_sec=50.0, event_definition_id=GOAL_ID, cap_number=7),
            LabeledEvent(time_sec=70.0, event_definition_id=GOAL_ID),
        ]
    )

    report = evaluate_draft(draft, labels)

    assert report.auto_confirmed_events == 3
    assert report.auto_confirmed_wrong == 2
    assert report.auto_confirmed_wrong_rate == pytest.approx(0.6667, abs=1e-4)


def test_auto_confirmed_wrong_ignores_cap_when_the_label_has_none() -> None:
    draft = EvalDraft(
        events=[
            EvalDraftEvent(
                video_time_sec=10.0,
                event_definition_id=GOAL_ID,
                resolved_cap_number=4,
                needs_review=False,
            )
        ]
    )
    labels = _labels([LabeledEvent(time_sec=10.0, event_definition_id=GOAL_ID)])

    report = evaluate_draft(draft, labels)

    assert report.auto_confirmed_wrong == 0
    assert report.auto_confirmed_wrong_rate == 0.0


def test_events_without_a_needs_review_field_are_not_treated_as_auto_confirmed() -> None:
    draft = EvalDraft(events=[EvalDraftEvent(video_time_sec=10.0, event_definition_id=STEAL_ID)])
    labels = _labels([LabeledEvent(time_sec=10.0, event_definition_id=GOAL_ID)])

    report = evaluate_draft(draft, labels)

    assert report.auto_confirmed_events == 0
    assert report.auto_confirmed_wrong_rate is None


def test_report_text_and_dict_expose_the_new_metrics() -> None:
    report = evaluate_draft(EvalDraft(), _labels([]))

    text = format_report(report)

    assert "Anchor precision:" in text
    assert "Auto-confirmed wrong:" in text
    payload = report.to_dict()
    assert {"anchor_precision", "auto_confirmed_wrong", "auto_confirmed_wrong_rate"} <= set(payload)
