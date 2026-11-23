"""Roboflow hosted / self-hosted Inference API detector adapter."""

from __future__ import annotations

import logging
from typing import Any

import cv2
import httpx
import numpy as np

from video_analyzer.detectors.base import BaseObjectDetector, Detection
from video_analyzer.detectors.class_map import ClassMapAdapter

logger = logging.getLogger(__name__)


class RoboflowDetector(BaseObjectDetector):
    """Object detector calling Roboflow Inference API (hosted or local Inference server)."""

    def __init__(
        self,
        model_id: str,
        api_key: str,
        api_url: str,
        confidence: float,
        iou: float,
        class_map: ClassMapAdapter,
        timeout_sec: float = 30.0,
        http_client: httpx.Client | None = None,
    ) -> None:
        self._model_id = model_id.strip("/")
        self._api_key = api_key.strip()
        self._api_url = api_url.rstrip("/")
        self._confidence = confidence
        self._iou = iou
        self._class_map = class_map
        self._timeout_sec = timeout_sec
        self._owns_client = http_client is None
        self._client = http_client or httpx.Client(timeout=timeout_sec)

    def close(self) -> None:
        """Close the owned HTTP client when tearing down the detector."""
        if self._owns_client:
            self._client.close()

    def provider_info(self) -> dict[str, str]:
        return {
            "provider": "roboflow",
            "model": self._model_id,
            "apiUrl": self._api_url,
        }

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        if not self._api_key:
            raise RuntimeError(
                "ROBOFLOW_API_KEY must be set when DETECTOR_PROVIDER=roboflow."
            )

        ok, encoded = cv2.imencode(".jpg", frame_bgr, [cv2.IMWRITE_JPEG_QUALITY, 90])
        if not ok:
            raise RuntimeError("Failed to JPEG-encode frame for Roboflow inference.")

        height, width = frame_bgr.shape[:2]
        endpoint = f"{self._api_url}/{self._model_id}"
        params = {
            "api_key": self._api_key,
            "confidence": round(self._confidence * 100.0, 2),
            "overlap": round(self._iou * 100.0, 2),
        }
        files = {"file": ("frame.jpg", encoded.tobytes(), "image/jpeg")}
        response = self._client.post(endpoint, params=params, files=files)
        response.raise_for_status()
        payload = response.json()
        return self.parse_predictions(payload, frame_width=width, frame_height=height)

    def parse_predictions(
        self,
        payload: dict[str, Any],
        frame_width: int,
        frame_height: int,
    ) -> list[Detection]:
        """Convert Roboflow center-xywh predictions into clamped Detection objects."""
        detections: list[Detection] = []
        for pred in payload.get("predictions", []):
            conf = float(pred.get("confidence", 0.0))
            if conf < self._confidence:
                continue
            cx = float(pred["x"])
            cy = float(pred["y"])
            box_w = float(pred["width"])
            box_h = float(pred["height"])
            x1 = max(0.0, cx - box_w / 2.0)
            y1 = max(0.0, cy - box_h / 2.0)
            x2 = min(float(frame_width), cx + box_w / 2.0)
            y2 = min(float(frame_height), cy + box_h / 2.0)
            if x2 <= x1 or y2 <= y1:
                continue

            raw_name = str(pred.get("class", "unknown"))
            role = self._class_map.map_class(raw_name)
            detections.append(
                Detection(
                    bbox_xyxy=(x1, y1, x2, y2),
                    confidence=conf,
                    role=role,
                    raw_class_name=raw_name,
                )
            )
        return detections
