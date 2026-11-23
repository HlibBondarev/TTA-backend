"""Pluggable object detector interfaces, adapters, and factory."""

from video_analyzer.detectors.base import BaseObjectDetector, CanonicalRole, Detection
from video_analyzer.detectors.class_map import ClassMapAdapter
from video_analyzer.detectors.factory import DetectorFactory
from video_analyzer.detectors.local_yolo import LocalYoloDetector
from video_analyzer.detectors.onnx_adapter import OnnxDetector
from video_analyzer.detectors.roboflow_adapter import RoboflowDetector

__all__ = [
    "BaseObjectDetector",
    "CanonicalRole",
    "ClassMapAdapter",
    "Detection",
    "DetectorFactory",
    "LocalYoloDetector",
    "OnnxDetector",
    "RoboflowDetector",
]
