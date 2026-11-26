"""Factory for instantiating the configured object detector strategy."""

from __future__ import annotations

from video_analyzer.config import Settings
from video_analyzer.detectors.base import BaseObjectDetector
from video_analyzer.detectors.class_map import ClassMapAdapter
from video_analyzer.detectors.local_yolo import LocalYoloDetector
from video_analyzer.detectors.onnx_adapter import OnnxDetector
from video_analyzer.detectors.roboflow_adapter import RoboflowDetector


class DetectorFactory:
    """Creates a concrete BaseObjectDetector from environment/config settings."""

    @staticmethod
    def create(settings: Settings) -> BaseObjectDetector:
        """Instantiate the configured detector adapter without requiring code edits."""
        class_map = ClassMapAdapter(settings.resolved_class_map)
        provider = settings.detector_provider.strip().lower()

        if provider == "roboflow":
            return RoboflowDetector(
                model_id=settings.detector_model_path_or_id,
                api_key=settings.roboflow_api_key,
                api_url=settings.roboflow_api_url,
                confidence=settings.detector_confidence,
                iou=settings.detector_iou,
                class_map=class_map,
                timeout_sec=settings.roboflow_timeout_sec,
                allow_insecure_local_http=settings.roboflow_allow_insecure_local_http,
                max_retries=settings.roboflow_max_retries,
                retry_base_sec=settings.roboflow_retry_base_sec,
            )

        if provider == "local_yolo":
            return LocalYoloDetector(
                model_path=settings.detector_model_path_or_id,
                confidence=settings.detector_confidence,
                iou=settings.detector_iou,
                device=settings.detector_device,
                class_map=class_map,
            )

        if provider == "onnx":
            return OnnxDetector(
                model_path=settings.detector_model_path_or_id,
                confidence=settings.detector_confidence,
                iou=settings.detector_iou,
                class_map=class_map,
                input_size=settings.detector_onnx_input_size,
                output_layout=settings.detector_onnx_output_layout,
            )

        raise ValueError(
            f"Unsupported DETECTOR_PROVIDER '{settings.detector_provider}'. "
            "Supported providers: 'roboflow', 'local_yolo', 'onnx'."
        )
