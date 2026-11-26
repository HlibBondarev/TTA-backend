from __future__ import annotations

from unittest.mock import patch

import numpy as np
import pytest

from video_analyzer.detectors.class_map import ClassMapAdapter
from video_analyzer.detectors.onnx_adapter import OnnxDetector

_SCORE_RANGE_HINT = "_transpose_hint_from_score_ranges"


def _build_detector() -> OnnxDetector:
    return OnnxDetector(
        model_path="dummy.onnx",
        confidence=0.3,
        iou=0.45,
        class_map=ClassMapAdapter({"0": "player", "1": "ball"}),
    )


@pytest.mark.parametrize("class_hint", [True, False])
def test_score_range_hint_is_skipped_when_class_count_resolves_orientation(
    class_hint: bool,
) -> None:
    detector = _build_detector()
    tensor = np.zeros((6, 20), dtype=np.float32)

    with (
        patch.object(detector, "_transpose_hint_from_class_count", return_value=class_hint),
        patch.object(detector, "_transpose_hint_from_declared_shape") as declared_hint,
        patch.object(detector, _SCORE_RANGE_HINT) as score_hint,
    ):
        result = detector._should_transpose_yolo_tensor(tensor)

    assert result is class_hint
    declared_hint.assert_not_called()
    score_hint.assert_not_called()


def test_score_range_hint_is_skipped_when_declared_shape_resolves_orientation() -> None:
    detector = _build_detector()
    tensor = np.zeros((6, 20), dtype=np.float32)

    with (
        patch.object(detector, "_transpose_hint_from_class_count", return_value=None),
        patch.object(detector, "_transpose_hint_from_declared_shape", return_value=False),
        patch.object(detector, _SCORE_RANGE_HINT) as score_hint,
    ):
        result = detector._should_transpose_yolo_tensor(tensor)

    assert result is False
    score_hint.assert_not_called()


def test_score_range_hint_is_used_when_earlier_hints_return_none() -> None:
    detector = _build_detector()
    tensor = np.zeros((6, 20), dtype=np.float32)

    with (
        patch.object(detector, "_transpose_hint_from_class_count", return_value=None),
        patch.object(detector, "_transpose_hint_from_declared_shape", return_value=None),
        patch.object(detector, _SCORE_RANGE_HINT, return_value=False) as score_hint,
    ):
        result = detector._should_transpose_yolo_tensor(tensor)

    assert result is False
    score_hint.assert_called_once_with(tensor)
