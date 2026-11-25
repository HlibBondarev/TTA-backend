"""Abstract strategy interface and canonical domain types for object detectors."""

from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from enum import StrEnum

import numpy as np


class CanonicalRole(StrEnum):
    """Canonical internal entity roles decoupled from model-specific label names."""

    PLAYER = "player"
    CAP = "cap"
    BALL = "ball"
    GOAL = "goal"
    REFEREE = "referee"
    UNKNOWN = "unknown"


@dataclass(frozen=True, slots=True)
class Detection:
    """Single object detection in pixel xyxy coordinates."""

    bbox_xyxy: tuple[float, float, float, float]
    confidence: float
    role: CanonicalRole
    raw_class_name: str


class BaseObjectDetector(ABC):
    """Strategy interface implemented by all object detection providers."""

    # Remote providers whose calls may safely overlap set this to True; local providers
    # (ONNX / YOLO) keep the default and are run serially by the orchestrator.
    supports_concurrent_inference: bool = False

    @abstractmethod
    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        """Run object detection on a BGR image frame and return canonical detections."""

    def warmup(self) -> None:
        """Optional model warmup hook."""
        return None

    @abstractmethod
    def provider_info(self) -> dict[str, str]:
        """Return metadata describing the active detector provider and model."""
