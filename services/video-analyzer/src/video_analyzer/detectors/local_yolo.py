"""Ultralytics local YOLO detector adapter."""

from __future__ import annotations

from typing import Any

import numpy as np

from video_analyzer.detectors.base import BaseObjectDetector, Detection
from video_analyzer.detectors.class_map import ClassMapAdapter


class LocalYoloDetector(BaseObjectDetector):
    """Object detector backed by local Ultralytics YOLO weights (.pt)."""

    def __init__(
        self,
        model_path: str,
        confidence: float,
        iou: float,
        device: str,
        class_map: ClassMapAdapter,
    ) -> None:
        self._model_path = model_path
        self._confidence = confidence
        self._iou = iou
        self._device = device
        self._class_map = class_map
        self._model: Any = None

    def provider_info(self) -> dict[str, str]:
        return {
            "provider": "local_yolo",
            "model": self._model_path,
            "device": self._device,
        }

    def warmup(self) -> None:
        self._ensure_loaded()

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        model = self._ensure_loaded()
        predict_kwargs: dict[str, Any] = {
            "source": frame_bgr,
            "conf": self._confidence,
            "iou": self._iou,
            "verbose": False,
        }
        if self._device and self._device != "auto":
            predict_kwargs["device"] = self._device

        results = model.predict(**predict_kwargs)
        if not results:
            return []

        result = results[0]
        names = getattr(result, "names", {}) or {}
        boxes = getattr(result, "boxes", None)
        if boxes is None:
            return []

        xyxy_arr = boxes.xyxy.cpu().numpy()
        conf_arr = boxes.conf.cpu().numpy()
        cls_arr = boxes.cls.cpu().numpy()

        detections: list[Detection] = []
        for idx in range(len(xyxy_arr)):
            x1, y1, x2, y2 = (float(v) for v in xyxy_arr[idx])
            conf = float(conf_arr[idx])
            cls_id = int(cls_arr[idx])
            raw_name = str(names.get(cls_id, str(cls_id)))
            role = self._class_map.map_class(raw_name, fallback_id=cls_id)
            detections.append(
                Detection(
                    bbox_xyxy=(x1, y1, x2, y2),
                    confidence=conf,
                    role=role,
                    raw_class_name=raw_name,
                )
            )
        return detections

    def _ensure_loaded(self) -> Any:
        if self._model is None:
            try:
                from ultralytics import YOLO
            except ImportError as exc:
                raise RuntimeError(
                    "ultralytics is required for DETECTOR_PROVIDER=local_yolo. "
                    "Install it with: pip install '.[yolo]'"
                ) from exc
            self._model = YOLO(self._model_path)
        return self._model
