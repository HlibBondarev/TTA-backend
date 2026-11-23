"""Tests for zero-code detector provider switching and canonical class mapping."""

from __future__ import annotations

import json
from pathlib import Path
from types import SimpleNamespace

import httpx
import numpy as np
import pytest

from video_analyzer.config import Settings
from video_analyzer.detectors.base import CanonicalRole
from video_analyzer.detectors.class_map import ClassMapAdapter
from video_analyzer.detectors.factory import DetectorFactory
from video_analyzer.detectors.local_yolo import LocalYoloDetector
from video_analyzer.detectors.onnx_adapter import OnnxDetector
from video_analyzer.detectors.roboflow_adapter import RoboflowDetector


def test_detector_factory_switches_providers_via_settings(tmp_path: Path) -> None:
    rf_settings = Settings(
        detector_provider="roboflow",
        detector_model_path_or_id="waterpolo-salvasc/5",
        roboflow_api_key="rf_test_key",
    )
    rf_detector = DetectorFactory.create(rf_settings)
    assert isinstance(rf_detector, RoboflowDetector)
    assert rf_detector.provider_info()["model"] == "waterpolo-salvasc/5"

    yolo_settings = Settings(
        detector_provider="local_yolo",
        detector_model_path_or_id="yolo11m.pt",
        detector_class_map='{"person":"player","sports ball":"ball"}',
    )
    yolo_detector = DetectorFactory.create(yolo_settings)
    assert isinstance(yolo_detector, LocalYoloDetector)
    assert yolo_detector.provider_info()["model"] == "yolo11m.pt"

    class_map_file = tmp_path / "classes.json"
    class_map_file.write_text(json.dumps({"0": "player", "1": "ball"}), encoding="utf-8")
    onnx_settings = Settings(
        detector_provider="onnx",
        detector_model_path_or_id="model.onnx",
        detector_class_map=str(class_map_file),
    )
    onnx_detector = DetectorFactory.create(onnx_settings)
    assert isinstance(onnx_detector, OnnxDetector)
    assert onnx_detector.provider_info()["provider"] == "onnx"


def test_detector_factory_rejects_unknown_provider() -> None:
    settings = Settings.model_construct(
        detector_provider="unknown_backend",
        detector_class_map={"person": "player"},
    )
    with pytest.raises(ValueError, match="Unsupported DETECTOR_PROVIDER"):
        DetectorFactory.create(settings)


def test_class_map_adapter_handles_case_and_fallback_ids() -> None:
    adapter = ClassMapAdapter(
        {
            "White team": "player",
            "Blue team": "player",
            "Goalkeeper": "player",
            "ball": "ball",
            "Goal": "goal",
            "Referee": "referee",
            "0": "player",
        }
    )
    assert adapter.map_class("white team") == CanonicalRole.PLAYER
    assert adapter.map_class("GOALKEEPER") == CanonicalRole.PLAYER
    assert adapter.map_class("Ball") == CanonicalRole.BALL
    assert adapter.map_class("Goal") == CanonicalRole.GOAL
    assert adapter.map_class("Referee") == CanonicalRole.REFEREE
    assert adapter.map_class("unmapped_name", fallback_id=0) == CanonicalRole.PLAYER
    assert adapter.map_class("spectator") == CanonicalRole.UNKNOWN

    with pytest.raises(ValueError, match="Invalid canonical role"):
        ClassMapAdapter({"swimmer": "not_a_role"})


def test_roboflow_detector_parses_predictions_and_requires_api_key() -> None:
    captured: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured["url"] = str(request.url)
        return httpx.Response(
            200,
            json={
                "predictions": [
                    {
                        "x": 500.0,
                        "y": 300.0,
                        "width": 120.0,
                        "height": 200.0,
                        "confidence": 0.88,
                        "class": "White team",
                    },
                    {
                        "x": 100.0,
                        "y": 100.0,
                        "width": 20.0,
                        "height": 20.0,
                        "confidence": 0.10,
                        "class": "ball",
                    },
                ]
            },
        )

    client = httpx.Client(transport=httpx.MockTransport(handler))
    detector = RoboflowDetector(
        model_id="waterpolo-salvasc/5",
        api_key="test-key",
        api_url="https://detect.roboflow.com",
        confidence=0.25,
        iou=0.45,
        class_map=ClassMapAdapter({"White team": "player", "ball": "ball"}),
        http_client=client,
    )
    frame = np.zeros((720, 1280, 3), dtype=np.uint8)
    detections = detector.detect(frame)

    assert "confidence=25.0" in str(captured["url"])
    assert "overlap=45.0" in str(captured["url"])
    assert len(detections) == 1
    assert detections[0].role == CanonicalRole.PLAYER
    assert detections[0].raw_class_name == "White team"
    assert detections[0].bbox_xyxy == (440.0, 200.0, 560.0, 400.0)

    detector_no_key = RoboflowDetector(
        model_id="waterpolo-salvasc/5",
        api_key="",
        api_url="https://detect.roboflow.com",
        confidence=0.25,
        iou=0.45,
        class_map=ClassMapAdapter({"White team": "player"}),
        http_client=client,
    )
    with pytest.raises(RuntimeError, match="ROBOFLOW_API_KEY"):
        detector_no_key.detect(frame)


def test_onnx_detector_decodes_standard_and_nms_tensors() -> None:
    detector = OnnxDetector(
        model_path="dummy.onnx",
        confidence=0.3,
        iou=0.45,
        class_map=ClassMapAdapter({"0": "player", "1": "ball"}),
        input_size=640,
    )
    # Standard YOLO export shape [1, 4 + 2, 20]
    raw_yolo = np.zeros((1, 6, 20), dtype=np.float32)
    raw_yolo[0, :, 0] = [320.0, 320.0, 80.0, 120.0, 0.90, 0.05]
    raw_yolo[0, :, 1] = [100.0, 100.0, 20.0, 20.0, 0.05, 0.15]

    dets = detector.decode_outputs(
        raw_yolo, orig_width=1280, orig_height=720, scale=0.5, pad_x=0.0, pad_y=140.0
    )
    assert len(dets) == 1
    assert dets[0].role == CanonicalRole.PLAYER
    assert dets[0].confidence == pytest.approx(0.90, abs=1e-4)

    # End-to-end NMS ONNX export shape [1, 1, 6]
    raw_nms = np.array([[[100.0, 200.0, 180.0, 320.0, 0.85, 1.0]]], dtype=np.float32)
    nms_dets = detector.decode_outputs(
        raw_nms, orig_width=1280, orig_height=720, scale=0.5, pad_x=0.0, pad_y=140.0
    )
    assert len(nms_dets) == 1
    assert nms_dets[0].role == CanonicalRole.BALL


def test_local_yolo_detector_parses_mock_ultralytics_output() -> None:
    class FakeArray:
        def __init__(self, data: np.ndarray) -> None:
            self._data = data

        def cpu(self) -> FakeArray:
            return self

        def numpy(self) -> np.ndarray:
            return self._data

    class FakeYoloModel:
        def predict(self, **kwargs):
            boxes = SimpleNamespace(
                xyxy=FakeArray(np.array([[10.0, 20.0, 110.0, 220.0]], dtype=np.float32)),
                conf=FakeArray(np.array([0.92], dtype=np.float32)),
                cls=FakeArray(np.array([0.0], dtype=np.float32)),
            )
            return [SimpleNamespace(names={0: "person"}, boxes=boxes)]

    detector = LocalYoloDetector(
        model_path="yolo11m.pt",
        confidence=0.25,
        iou=0.45,
        device="cpu",
        class_map=ClassMapAdapter({"person": "player"}),
    )
    detector._model = FakeYoloModel()
    dets = detector.detect(np.zeros((480, 640, 3), dtype=np.uint8))

    assert len(dets) == 1
    assert dets[0].role == CanonicalRole.PLAYER
    assert dets[0].raw_class_name == "person"
    assert dets[0].confidence == pytest.approx(0.92, abs=1e-4)
