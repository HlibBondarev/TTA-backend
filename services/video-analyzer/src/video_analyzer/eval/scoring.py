"""Offline scoring of a generated draft against ground-truth labels (no network, no API calls).

Metric definitions
------------------
* Events are matched one-to-one by time only (greedy, closest pair first, within the tolerance).
* ``event_precision`` = matched / predicted events; ``event_recall`` = matched / labeled events.
* ``label_accuracy`` = matched pairs whose ``eventDefinitionId`` is equal / matched pairs.
* ``anchor_accuracy`` = labeled anchors matched by type (and period when both are known) within
  the tolerance / labeled anchors.
* ``anchor_precision`` = predicted anchors matched to a labeled anchor / predicted anchors.
* ``auto_confirmed_wrong`` = matched events the pipeline auto-confirmed (``needsReview`` is
  false) whose ``eventDefinitionId`` differs from the label or whose resolved cap number differs
  from a labeled ``capNumber``; ``auto_confirmed_wrong_rate`` divides it by the matched events
  that were auto-confirmed.
* ``cap_resolution_accuracy`` = matched pairs whose draft ``resolvedCapNumber`` equals the label's
  ``capNumber`` / matched pairs whose label has a ``capNumber`` (unresolved counts as incorrect).

A ratio whose denominator is zero is reported as ``None`` (rendered as ``n/a``).
"""

from __future__ import annotations

import math
from collections.abc import Callable, Sequence
from dataclasses import asdict, dataclass

from video_analyzer.eval.schema import (
    EvalDraft,
    EvalDraftAnchor,
    EvalDraftEvent,
    GroundTruthLabels,
    LabeledAnchor,
    LabeledEvent,
)

DEFAULT_TOLERANCE_SEC: float = 3.0


@dataclass(frozen=True, slots=True)
class EvaluationReport:
    """Aggregate offline evaluation metrics for one draft/label pair."""

    tolerance_sec: float
    predicted_events: int
    labeled_events: int
    matched_events: int
    event_precision: float | None
    event_recall: float | None
    label_correct: int
    label_accuracy: float | None
    predicted_anchors: int
    labeled_anchors: int
    matched_anchors: int
    anchor_accuracy: float | None
    anchor_precision: float | None
    auto_confirmed_events: int
    auto_confirmed_wrong: int
    auto_confirmed_wrong_rate: float | None
    cap_labeled_matches: int
    cap_correct: int
    cap_resolution_accuracy: float | None

    def to_dict(self) -> dict[str, float | int | None]:
        """Return the report as a JSON-serializable dictionary."""
        return asdict(self)


def _ratio(numerator: int, denominator: int) -> float | None:
    return round(numerator / denominator, 4) if denominator > 0 else None


def match_by_time(
    predicted_times: Sequence[float],
    labeled_times: Sequence[float],
    tolerance_sec: float,
    is_compatible: Callable[[int, int], bool] | None = None,
) -> list[tuple[int, int]]:
    """Greedily pair predictions with labels one-to-one, closest time difference first.

    Ties are broken by prediction index then label index, so the result is deterministic.

    Returns:
        ``(predicted_index, labeled_index)`` pairs sorted by predicted index.
    """
    candidates = sorted(
        (abs(p_time - l_time), p_idx, l_idx)
        for p_idx, p_time in enumerate(predicted_times)
        for l_idx, l_time in enumerate(labeled_times)
        if abs(p_time - l_time) <= tolerance_sec
        and (is_compatible is None or is_compatible(p_idx, l_idx))
    )
    used_pred: set[int] = set()
    used_label: set[int] = set()
    pairs: list[tuple[int, int]] = []
    for _, p_idx, l_idx in candidates:
        if p_idx in used_pred or l_idx in used_label:
            continue
        used_pred.add(p_idx)
        used_label.add(l_idx)
        pairs.append((p_idx, l_idx))
    return sorted(pairs)


def _anchors_compatible(predicted: EvalDraftAnchor, labeled: LabeledAnchor) -> bool:
    if predicted.type != labeled.type:
        return False
    if predicted.period_number is None or labeled.period_number is None:
        return True
    return predicted.period_number == labeled.period_number


def _is_wrong(predicted: EvalDraftEvent, labeled: LabeledEvent) -> bool:
    """Return True when the predicted label or (if labeled) cap number is incorrect."""
    if predicted.event_definition_id != labeled.event_definition_id:
        return True
    return labeled.cap_number is not None and predicted.resolved_cap_number != labeled.cap_number


def _score_auto_confirmed(
    predicted: list[EvalDraftEvent],
    labeled: list[LabeledEvent],
    pairs: list[tuple[int, int]],
) -> tuple[int, int]:
    """Return (auto-confirmed matched events, those of them with a wrong label or cap)."""
    confirmed = [(p, lb) for p, lb in pairs if predicted[p].needs_review is False]
    wrong = sum(1 for p_idx, l_idx in confirmed if _is_wrong(predicted[p_idx], labeled[l_idx]))
    return len(confirmed), wrong


def _score_events(
    predicted: list[EvalDraftEvent],
    labeled: list[LabeledEvent],
    tolerance_sec: float,
) -> tuple[list[tuple[int, int]], int, int, int]:
    pairs = match_by_time(
        [event.video_time_sec for event in predicted],
        [event.time_sec for event in labeled],
        tolerance_sec,
    )
    label_correct = sum(
        1
        for p_idx, l_idx in pairs
        if predicted[p_idx].event_definition_id == labeled[l_idx].event_definition_id
    )
    cap_pairs = [(p, lb) for p, lb in pairs if labeled[lb].cap_number is not None]
    cap_correct = sum(
        1
        for p_idx, l_idx in cap_pairs
        if predicted[p_idx].resolved_cap_number == labeled[l_idx].cap_number
    )
    return pairs, label_correct, len(cap_pairs), cap_correct


def _score_anchors(
    predicted: list[EvalDraftAnchor],
    labeled: list[LabeledAnchor],
    tolerance_sec: float,
) -> int:
    pairs = match_by_time(
        [anchor.video_time_sec for anchor in predicted],
        [anchor.time_sec for anchor in labeled],
        tolerance_sec,
        is_compatible=lambda p_idx, l_idx: _anchors_compatible(predicted[p_idx], labeled[l_idx]),
    )
    return len(pairs)


def evaluate_draft(
    draft: EvalDraft,
    labels: GroundTruthLabels,
    tolerance_sec: float = DEFAULT_TOLERANCE_SEC,
) -> EvaluationReport:
    """Score a generated draft against ground-truth labels.

    Args:
        draft: Lenient view of the draft JSON produced by ``tta-video-analyzer analyze``.
        labels: Coach-provided ground truth for the same clip.
        tolerance_sec: Maximum |time difference| for an event or anchor to count as a match.

    Raises:
        ValueError: If ``tolerance_sec`` is not finite or is negative.
    """
    if not math.isfinite(tolerance_sec) or tolerance_sec < 0:
        raise ValueError("tolerance_sec must be a finite, non-negative number.")

    pairs, label_correct, cap_labeled, cap_correct = _score_events(
        draft.events, labels.events, tolerance_sec
    )
    matched_anchors = _score_anchors(draft.time_anchors, labels.anchors, tolerance_sec)
    confirmed, confirmed_wrong = _score_auto_confirmed(draft.events, labels.events, pairs)
    return EvaluationReport(
        tolerance_sec=tolerance_sec,
        predicted_events=len(draft.events),
        labeled_events=len(labels.events),
        matched_events=len(pairs),
        event_precision=_ratio(len(pairs), len(draft.events)),
        event_recall=_ratio(len(pairs), len(labels.events)),
        label_correct=label_correct,
        label_accuracy=_ratio(label_correct, len(pairs)),
        predicted_anchors=len(draft.time_anchors),
        labeled_anchors=len(labels.anchors),
        matched_anchors=matched_anchors,
        anchor_accuracy=_ratio(matched_anchors, len(labels.anchors)),
        anchor_precision=_ratio(matched_anchors, len(draft.time_anchors)),
        auto_confirmed_events=confirmed,
        auto_confirmed_wrong=confirmed_wrong,
        auto_confirmed_wrong_rate=_ratio(confirmed_wrong, confirmed),
        cap_labeled_matches=cap_labeled,
        cap_correct=cap_correct,
        cap_resolution_accuracy=_ratio(cap_correct, cap_labeled),
    )


def _format_metric(value: float | None, numerator: int, denominator: int) -> str:
    if value is None:
        return f"n/a ({numerator}/{denominator})"
    return f"{value:.1%} ({numerator}/{denominator})"


def format_report(report: EvaluationReport, clip_id: str | None = None) -> str:
    """Render the report as a short human-readable block for the CLI."""
    header = f"Evaluation (tolerance +/-{report.tolerance_sec:g}s)"
    if clip_id:
        header += f" for clip '{clip_id}'"
    lines = [
        header,
        "  Event precision:         "
        + _format_metric(report.event_precision, report.matched_events, report.predicted_events),
        "  Event recall:            "
        + _format_metric(report.event_recall, report.matched_events, report.labeled_events),
        "  Label accuracy:          "
        + _format_metric(report.label_accuracy, report.label_correct, report.matched_events),
        "  Anchor accuracy:         "
        + _format_metric(report.anchor_accuracy, report.matched_anchors, report.labeled_anchors),
        "  Anchor precision:        "
        + _format_metric(report.anchor_precision, report.matched_anchors, report.predicted_anchors),
        "  Auto-confirmed wrong:    "
        + _format_metric(
            report.auto_confirmed_wrong_rate,
            report.auto_confirmed_wrong,
            report.auto_confirmed_events,
        ),
        "  Cap resolution accuracy: "
        + _format_metric(
            report.cap_resolution_accuracy, report.cap_correct, report.cap_labeled_matches
        ),
    ]
    return "\n".join(lines)
