"""Tests for zero-code detector provider switching and canonical class mapping."""

from __future__ import annotations

import json
import logging
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from types import SimpleNamespace

import httpx
import numpy as np
import pytest
from pydantic import ValidationError

from video_analyzer.config import Settings
from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.detectors.class_map import ClassMapAdapter
from video_analyzer.detectors.concurrency import ConcurrencyLimitedDetector, TimedDetector
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


def test_onnx_nms_runs_per_original_class_and_keeps_overlapping_distinct_classes() -> None:
    detector = OnnxDetector(
        model_path="dummy.onnx",
        confidence=0.3,
        iou=0.45,
        class_map=ClassMapAdapter({"0": "player", "1": "player"}),
        input_size=640,
    )
    # Standard YOLO export shape [1, 4 + 2, 20]:
    # two overlapping boxes from class 0 (duplicate), one overlapping box from class 1 (distinct)
    raw_yolo = np.zeros((1, 6, 20), dtype=np.float32)
    raw_yolo[0, :, 0] = [320.0, 320.0, 100.0, 140.0, 0.90, 0.05]
    raw_yolo[0, :, 1] = [322.0, 322.0, 100.0, 140.0, 0.70, 0.05]  # same class 0 -> suppressed
    raw_yolo[0, :, 2] = [320.0, 320.0, 100.0, 140.0, 0.05, 0.85]  # class 1 -> kept

    dets = detector.decode_outputs(
        raw_yolo, orig_width=640, orig_height=640, scale=1.0, pad_x=0.0, pad_y=0.0
    )
    assert len(dets) == 2
    assert {d.raw_class_name for d in dets} == {"0", "1"}


def test_roboflow_detector_falls_back_to_numeric_class_id() -> None:
    detector = RoboflowDetector(
        model_id="waterpolo-salvasc/5",
        api_key="test-key",
        api_url="https://detect.roboflow.com",
        confidence=0.25,
        iou=0.45,
        class_map=ClassMapAdapter({"0": "player", "1": "ball"}),
    )
    dets = detector.parse_predictions(
        payload={
            "predictions": [
                {
                    "x": 320.0,
                    "y": 240.0,
                    "width": 80.0,
                    "height": 120.0,
                    "confidence": 0.9,
                    "class": "unmapped_custom_label",
                    "class_id": 0,
                }
            ]
        },
        frame_width=640,
        frame_height=480,
    )
    assert len(dets) == 1
    assert dets[0].role == CanonicalRole.PLAYER
    assert dets[0].raw_class_name == "unmapped_custom_label"


def test_onnx_equal_axis_output_layout_handles_channels_first_and_last() -> None:
    class_map = ClassMapAdapter({"0": "player", "1": "ball"})

    # Case 1: Equal-axis [1, 84, 84] in channels_first ([4 + 80, 84])
    cf_tensor = np.zeros((1, 84, 84), dtype=np.float32)
    cf_tensor[0, :4, 0] = [320.0, 320.0, 80.0, 120.0]
    cf_tensor[0, 5, 0] = 0.91  # class 1

    detector_auto = OnnxDetector(
        model_path="dummy.onnx",
        confidence=0.3,
        iou=0.45,
        class_map=class_map,
        input_size=640,
    )
    cf_dets = detector_auto.decode_outputs(
        cf_tensor, orig_width=640, orig_height=640, scale=1.0, pad_x=0.0, pad_y=0.0
    )
    assert len(cf_dets) == 1
    assert cf_dets[0].role == CanonicalRole.BALL
    assert cf_dets[0].confidence == pytest.approx(0.91, abs=1e-4)

    # Case 2: Equal-axis [1, 84, 84] in channels_last ([84, 4 + 80]) with candidate idx >= 4
    cl_tensor = np.zeros((1, 84, 84), dtype=np.float32)
    cl_tensor[0, 5, :4] = [320.0, 320.0, 80.0, 120.0]
    cl_tensor[0, 5, 4] = 0.88  # class 0

    cl_dets = detector_auto.decode_outputs(
        cl_tensor, orig_width=640, orig_height=640, scale=1.0, pad_x=0.0, pad_y=0.0
    )
    assert len(cl_dets) == 1
    assert cl_dets[0].role == CanonicalRole.PLAYER
    assert cl_dets[0].confidence == pytest.approx(0.88, abs=1e-4)

    # Case 3: Channels-first [1, 84, 84] with sub-threshold scores and coords in [conf, 1.0]
    # Must remain channels_first in auto mode and yield zero false detections.
    cf_subthreshold = np.zeros((1, 84, 84), dtype=np.float32)
    cf_subthreshold[0, :4, 10] = [0.5, 0.6, 0.4, 0.5]
    cf_subthreshold[0, 4:, 10] = 0.05
    assert (
        detector_auto.decode_outputs(
            cf_subthreshold, orig_width=640, orig_height=640, scale=1.0, pad_x=0.0, pad_y=0.0
        )
        == []
    )

    # Case 4: DetectorFactory exposes DETECTOR_ONNX_OUTPUT_LAYOUT for explicit overrides
    factory_cl = DetectorFactory.create(
        Settings(
            _env_file=None,
            detector_provider="onnx",
            detector_model_path_or_id="dummy.onnx",
            detector_confidence=0.3,
            detector_iou=0.45,
            detector_onnx_output_layout=" CHANNELS_LAST ",
            detector_class_map={"0": "player", "1": "ball"},
        )
    )
    assert isinstance(factory_cl, OnnxDetector)
    assert factory_cl.provider_info()["outputLayout"] == "channels_last"

    cl_idx0_tensor = np.zeros((1, 84, 84), dtype=np.float32)
    cl_idx0_tensor[0, 0, :4] = [320.0, 320.0, 80.0, 120.0]
    cl_idx0_tensor[0, 0, 4] = 0.88
    cl_idx0_dets = factory_cl.decode_outputs(
        cl_idx0_tensor, orig_width=640, orig_height=640, scale=1.0, pad_x=0.0, pad_y=0.0
    )
    assert len(cl_idx0_dets) == 1
    assert cl_idx0_dets[0].role == CanonicalRole.PLAYER

    # Explicit channels_last also prevents raw [1, N, 6] (2 classes) from being treated as NMS
    raw_cl_two_class = np.zeros((1, 20, 6), dtype=np.float32)
    raw_cl_two_class[0, 0, :] = [320.0, 320.0, 80.0, 120.0, 0.05, 0.89]
    raw_cl_dets = factory_cl.decode_outputs(
        raw_cl_two_class, orig_width=640, orig_height=640, scale=1.0, pad_x=0.0, pad_y=0.0
    )
    assert len(raw_cl_dets) == 1
    assert raw_cl_dets[0].role == CanonicalRole.BALL
    assert raw_cl_dets[0].confidence == pytest.approx(0.89, abs=1e-4)

    # Explicit nms layout routes [1, N, 6] directly to NMS decoding
    factory_nms = DetectorFactory.create(
        Settings(
            _env_file=None,
            detector_provider="onnx",
            detector_model_path_or_id="dummy.onnx",
            detector_confidence=0.3,
            detector_onnx_output_layout="nms",
            detector_class_map={"0": "player", "1": "ball"},
        )
    )
    nms_dets = factory_nms.decode_outputs(
        np.array([[[100.0, 200.0, 180.0, 320.0, 0.85, 1.0]]], dtype=np.float32),
        orig_width=1280,
        orig_height=720,
        scale=0.5,
        pad_x=0.0,
        pad_y=140.0,
    )
    assert len(nms_dets) == 1
    assert nms_dets[0].role == CanonicalRole.BALL

    with pytest.raises(ValidationError):
        Settings(_env_file=None, detector_onnx_output_layout="invalid_layout")


def test_roboflow_rejects_non_https_unless_explicit_local_loopback_http() -> None:
    with pytest.raises(ValidationError, match="https://"):
        Settings(_env_file=None, roboflow_api_url="http://detect.roboflow.com")

    with pytest.raises(ValidationError, match="https://"):
        Settings(_env_file=None, roboflow_api_url="http://localhost:9001")

    with pytest.raises(ValidationError, match="https://"):
        Settings(
            _env_file=None,
            roboflow_api_url="http://remote.example.com:9001",
            roboflow_allow_insecure_local_http=True,
        )

    local_settings = Settings(
        _env_file=None,
        roboflow_api_url="http://127.0.0.1:9001/",
        roboflow_allow_insecure_local_http=True,
    )
    assert local_settings.roboflow_api_url == "http://127.0.0.1:9001"

    with pytest.raises(ValueError, match="https://"):
        RoboflowDetector(
            model_id="waterpolo-salvasc/5",
            api_key="test-key",
            api_url="http://localhost:9001",
            confidence=0.25,
            iou=0.45,
            class_map=ClassMapAdapter({"White team": "player"}),
        )


def test_roboflow_redacts_api_key_in_httpx_logs_and_errors(
    caplog: pytest.LogCaptureFixture,
) -> None:
    secret_key = "rf_super_secret_key_value"

    def ok_handler(request: httpx.Request) -> httpx.Response:
        # Simulate httpx INFO request log carrying the URL with api_key
        logging.getLogger("httpx").info(
            'HTTP Request: POST %s "HTTP/1.1 200 OK"',
            str(request.url),
        )
        return httpx.Response(200, json={"predictions": []})

    client = httpx.Client(transport=httpx.MockTransport(ok_handler))
    detector = RoboflowDetector(
        model_id="waterpolo-salvasc/5",
        api_key=secret_key,
        api_url="https://detect.roboflow.com",
        confidence=0.25,
        iou=0.45,
        class_map=ClassMapAdapter({"White team": "player"}),
        http_client=client,
    )
    frame = np.zeros((120, 160, 3), dtype=np.uint8)

    with caplog.at_level(logging.INFO, logger="httpx"):
        detector.detect(frame)

    # Default httpx level was raised to WARNING, and any explicit INFO log redacts api_key
    assert secret_key not in caplog.text

    # Force INFO directly on httpx logger to confirm the filter redacts api_key if re-enabled
    logging.getLogger("httpx").setLevel(logging.INFO)
    caplog.clear()
    with caplog.at_level(logging.INFO, logger="httpx"):
        detector.detect(frame)
    assert secret_key not in caplog.text
    assert "[REDACTED]" in caplog.text

    def error_handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(403, request=request)

    err_detector = RoboflowDetector(
        model_id="waterpolo-salvasc/5",
        api_key=secret_key,
        api_url="https://detect.roboflow.com",
        confidence=0.25,
        iou=0.45,
        class_map=ClassMapAdapter({"White team": "player"}),
        http_client=httpx.Client(transport=httpx.MockTransport(error_handler)),
    )
    with pytest.raises(RuntimeError) as exc_info:
        err_detector.detect(frame)
    assert secret_key not in str(exc_info.value)
    assert "[REDACTED]" in str(exc_info.value)


def test_onnx_detector_serializes_lazy_initialization_across_concurrent_threads() -> None:
    first_entered_get_inputs = threading.Event()
    release_get_inputs = threading.Event()

    class SlowMetadataSession:
        def __init__(self) -> None:
            self.get_inputs_calls = 0

        def get_inputs(self):
            self.get_inputs_calls += 1
            first_entered_get_inputs.set()
            assert release_get_inputs.wait(timeout=3.0)
            return [SimpleNamespace(name="images")]

        def get_outputs(self):
            time.sleep(0.05)
            return [SimpleNamespace(shape=(1, 6, 84))]

        def get_modelmeta(self):
            time.sleep(0.05)
            return SimpleNamespace(custom_metadata_map={"names": '{"0": "ball", "1": "player"}'})

        def run(self, output_names, input_feed):
            assert "images" in input_feed
            # Tensor shape [1, 6, 6] is ambiguous without _output_shape and _class_names
            tensor = np.zeros((1, 6, 6), dtype=np.float32)
            tensor[0, :4, 0] = [320.0, 320.0, 80.0, 120.0]
            tensor[0, 4, 0] = 0.90  # class 0 -> ball from metadata
            return [tensor]

    session = SlowMetadataSession()
    detector = OnnxDetector(
        model_path="dummy.onnx",
        confidence=0.3,
        iou=0.45,
        class_map=ClassMapAdapter({"ball": "ball", "player": "player"}),
        input_size=640,
        session=session,
    )
    frame = np.zeros((640, 640, 3), dtype=np.uint8)

    with ThreadPoolExecutor(max_workers=2) as pool:
        future_a = pool.submit(detector.detect, frame)
        assert first_entered_get_inputs.wait(timeout=3.0)
        future_b = pool.submit(detector.detect, frame)
        time.sleep(0.05)
        release_get_inputs.set()
        dets_a = future_a.result(timeout=5.0)
        dets_b = future_b.result(timeout=5.0)

    assert session.get_inputs_calls == 1
    assert detector._output_shape == (1, 6, 84)
    assert detector._class_names == {0: "ball", 1: "player"}
    for dets in (dets_a, dets_b):
        assert len(dets) == 1
        assert dets[0].role == CanonicalRole.BALL
        assert dets[0].raw_class_name == "ball"


# ---------------------------------------------------------------------------------------------
# Issue #157: global concurrency limiter and Roboflow retry/backoff
# ---------------------------------------------------------------------------------------------


class _ProbeDetector(BaseObjectDetector):
    """Fake detector measuring simultaneous calls."""

    supports_concurrent_inference = True

    def __init__(self, delay_sec: float = 0.03) -> None:
        self._lock = threading.Lock()
        self._active = 0
        self.max_active = 0
        self.calls = 0
        self._delay_sec = delay_sec

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        with self._lock:
            self._active += 1
            self.calls += 1
            self.max_active = max(self.max_active, self._active)
        try:
            time.sleep(self._delay_sec)
            return []
        finally:
            with self._lock:
                self._active -= 1

    def provider_info(self) -> dict[str, str]:
        return {"provider": "probe", "model": "fake"}


def test_concurrency_limited_detector_caps_simultaneous_calls_across_all_callers() -> None:
    inner = _ProbeDetector()
    limited = ConcurrencyLimitedDetector(inner, max_concurrency=3)
    frame = np.zeros((8, 8, 3), dtype=np.uint8)

    # 12 independent callers (e.g. 12 Phase A tasks) share ONE semaphore.
    with ThreadPoolExecutor(max_workers=12) as pool:
        list(pool.map(lambda _: limited.detect(frame), range(12)))

    assert inner.calls == 12
    assert inner.max_active == 3
    assert limited.provider_info() == {"provider": "probe", "model": "fake"}
    assert limited.max_concurrency == 3
    assert limited.inner is inner

    serial_inner = _ProbeDetector()
    serial = ConcurrencyLimitedDetector(serial_inner, max_concurrency=1)
    with ThreadPoolExecutor(max_workers=6) as pool:
        list(pool.map(lambda _: serial.detect(frame), range(6)))
    assert serial_inner.max_active == 1

    with pytest.raises(ValueError, match="at least 1"):
        ConcurrencyLimitedDetector(inner, max_concurrency=0)


def test_timed_detector_accumulates_inference_time_and_delegates() -> None:
    inner = _ProbeDetector(delay_sec=0.03)
    timed = TimedDetector(inner)
    frame = np.zeros((8, 8, 3), dtype=np.uint8)
    timed.detect(frame)
    timed.detect(frame)
    assert timed.elapsed_sec >= 0.04
    assert timed.provider_info()["provider"] == "probe"


def test_only_remote_roboflow_detector_declares_concurrent_inference_support() -> None:
    assert RoboflowDetector.supports_concurrent_inference is True
    assert OnnxDetector.supports_concurrent_inference is False
    assert LocalYoloDetector.supports_concurrent_inference is False


def _roboflow(handler, **kwargs) -> tuple[RoboflowDetector, list[float]]:
    sleeps: list[float] = []
    detector = RoboflowDetector(
        model_id="waterpolo-salvasc/5",
        api_key="secret-rf-key",
        api_url="https://detect.roboflow.com",
        confidence=0.25,
        iou=0.45,
        class_map=ClassMapAdapter({"White team": "player"}),
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
        sleep=sleeps.append,
        **kwargs,
    )
    return detector, sleeps


_OK_PAYLOAD = {
    "predictions": [
        {
            "x": 50.0,
            "y": 50.0,
            "width": 20.0,
            "height": 40.0,
            "confidence": 0.9,
            "class": "White team",
        }
    ]
}


def test_roboflow_retries_transient_failures_with_backoff_then_succeeds() -> None:
    attempts = {"n": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        attempts["n"] += 1
        if attempts["n"] == 1:
            return httpx.Response(503, request=request)
        if attempts["n"] == 2:
            raise httpx.ConnectTimeout("slow", request=request)
        if attempts["n"] == 3:
            return httpx.Response(429, headers={"retry-after": "4"}, request=request)
        return httpx.Response(200, json=_OK_PAYLOAD)

    detector, sleeps = _roboflow(handler, max_retries=3, retry_base_sec=1.0)
    detections = detector.detect(np.zeros((100, 100, 3), dtype=np.uint8))

    assert attempts["n"] == 4
    assert len(detections) == 1
    assert len(sleeps) == 3
    assert 0.5 <= sleeps[0] <= 1.0  # attempt 0: 1s * [0.5, 1.0]
    assert 1.0 <= sleeps[1] <= 2.0  # attempt 1: 2s * [0.5, 1.0]
    assert sleeps[2] == 4.0  # honors Retry-After


def test_roboflow_does_not_retry_client_errors_and_gives_up_after_max_retries() -> None:
    attempts = {"n": 0}

    def forbidden(request: httpx.Request) -> httpx.Response:
        attempts["n"] += 1
        return httpx.Response(403, request=request)

    detector, sleeps = _roboflow(forbidden, max_retries=3)
    with pytest.raises(RuntimeError, match="request failed"):
        detector.detect(np.zeros((10, 10, 3), dtype=np.uint8))
    assert attempts["n"] == 1
    assert sleeps == []

    attempts["n"] = 0

    def always_500(request: httpx.Request) -> httpx.Response:
        attempts["n"] += 1
        return httpx.Response(500, request=request)

    detector, sleeps = _roboflow(always_500, max_retries=2)
    with pytest.raises(RuntimeError) as exc_info:
        detector.detect(np.zeros((10, 10, 3), dtype=np.uint8))
    assert attempts["n"] == 3
    assert len(sleeps) == 2
    assert "secret-rf-key" not in str(exc_info.value)

    detector, sleeps = _roboflow(always_500, max_retries=0)
    attempts["n"] = 0
    with pytest.raises(RuntimeError):
        detector.detect(np.zeros((10, 10, 3), dtype=np.uint8))
    assert attempts["n"] == 1
    assert sleeps == []

    with pytest.raises(ValueError, match="max_retries"):
        _roboflow(always_500, max_retries=-1)


def test_detector_factory_passes_concurrency_and_retry_settings() -> None:
    settings = Settings(
        _env_file=None,
        detector_provider="roboflow",
        roboflow_api_key="rf_test_key",
        roboflow_max_retries=5,
        roboflow_retry_base_sec=0.25,
    )
    detector = DetectorFactory.create(settings)
    assert isinstance(detector, RoboflowDetector)
    assert detector._max_retries == 5
    assert detector._retry_base_sec == 0.25
    assert (settings.detector_max_concurrency, settings.detector_input_width) == (4, 1280)
