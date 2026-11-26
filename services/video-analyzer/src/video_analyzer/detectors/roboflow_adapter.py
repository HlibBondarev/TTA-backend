"""Roboflow hosted / self-hosted Inference API detector adapter."""

from __future__ import annotations

import logging
import random
import time
from collections.abc import Callable
from typing import Any

import cv2
import httpx
import numpy as np

from video_analyzer.config import validate_roboflow_api_url
from video_analyzer.detectors.base import BaseObjectDetector, Detection
from video_analyzer.detectors.class_map import ClassMapAdapter
from video_analyzer.logging_utils import configure_safe_http_logging, redact_sensitive_text

logger = logging.getLogger(__name__)

_HTTP_TOO_MANY_REQUESTS = 429
_HTTP_SERVER_ERROR_MIN = 500
_MAX_BACKOFF_SEC = 30.0


class RoboflowDetector(BaseObjectDetector):
    """Object detector calling Roboflow Inference API (hosted or local Inference server).

    Transient failures (timeouts, transport errors, HTTP 429 and 5xx) are retried with jittered
    exponential backoff; other HTTP errors fail immediately. Calls are thread-safe, so the
    orchestrator may overlap them up to ``DETECTOR_MAX_CONCURRENCY``.
    """

    supports_concurrent_inference = True

    def __init__(
        self,
        model_id: str,
        api_key: str,
        api_url: str,
        confidence: float,
        iou: float,
        class_map: ClassMapAdapter,
        timeout_sec: float = 30.0,
        allow_insecure_local_http: bool = False,
        http_client: httpx.Client | None = None,
        max_retries: int = 3,
        retry_base_sec: float = 1.0,
        sleep: Callable[[float], None] = time.sleep,
    ) -> None:
        if max_retries < 0:
            raise ValueError("max_retries must be non-negative.")
        if retry_base_sec <= 0:
            raise ValueError("retry_base_sec must be positive.")
        configure_safe_http_logging()
        self._model_id = model_id.strip("/")
        self._api_key = api_key.strip()
        self._api_url = validate_roboflow_api_url(
            api_url,
            allow_insecure_local_http=allow_insecure_local_http,
        )
        self._confidence = confidence
        self._iou = iou
        self._class_map = class_map
        self._timeout_sec = timeout_sec
        self._max_retries = max_retries
        self._retry_base_sec = retry_base_sec
        self._sleep = sleep
        self._owns_client = http_client is None
        self._client = http_client or httpx.Client(timeout=timeout_sec, follow_redirects=False)

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
            raise RuntimeError("ROBOFLOW_API_KEY must be set when DETECTOR_PROVIDER=roboflow.")

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
        response = self._post_with_retry(endpoint, params, files)
        payload = response.json()
        return self.parse_predictions(payload, frame_width=width, frame_height=height)

    def _post_with_retry(
        self,
        endpoint: str,
        params: dict[str, Any],
        files: dict[str, Any],
    ) -> httpx.Response:
        for attempt in range(self._max_retries + 1):
            try:
                response = self._client.post(
                    endpoint,
                    params=params,
                    files=files,
                    follow_redirects=False,
                )
                response.raise_for_status()
                return response
            except httpx.HTTPError as exc:
                if attempt >= self._max_retries or not self._is_retriable(exc):
                    raise RuntimeError(
                        f"Roboflow inference request failed: {redact_sensitive_text(str(exc))}"
                    ) from None
                delay = self._backoff_delay(attempt, exc)
                logger.warning(
                    "Roboflow request failed (%s); retry %d/%d in %.1fs.",
                    type(exc).__name__,
                    attempt + 1,
                    self._max_retries,
                    delay,
                )
                self._sleep(delay)
        raise AssertionError("unreachable: retry loop always returns or raises")

    @staticmethod
    def _is_retriable(exc: httpx.HTTPError) -> bool:
        if isinstance(exc, httpx.HTTPStatusError):
            status = exc.response.status_code
            return status == _HTTP_TOO_MANY_REQUESTS or status >= _HTTP_SERVER_ERROR_MIN
        return isinstance(exc, httpx.TransportError)

    def _backoff_delay(self, attempt: int, exc: httpx.HTTPError) -> float:
        if isinstance(exc, httpx.HTTPStatusError):
            retry_after = exc.response.headers.get("retry-after", "")
            if retry_after.strip().isdigit():
                return min(_MAX_BACKOFF_SEC, float(retry_after))
        exponential = min(_MAX_BACKOFF_SEC, self._retry_base_sec * (2.0**attempt))
        return exponential * (0.5 + 0.5 * random.random())

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

            raw_class_id = pred.get("class_id")
            fallback_id: int | None = None
            if raw_class_id is not None and not isinstance(raw_class_id, bool):
                try:
                    fallback_id = int(raw_class_id)
                except (TypeError, ValueError):
                    fallback_id = None

            raw_class_val = pred.get("class")
            raw_name = str(raw_class_val) if raw_class_val is not None else "unknown"
            role = self._class_map.map_class(raw_name, fallback_id=fallback_id)
            detections.append(
                Detection(
                    bbox_xyxy=(x1, y1, x2, y2),
                    confidence=conf,
                    role=role,
                    raw_class_name=raw_name,
                )
            )
        return detections
