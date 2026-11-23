"""ONNX Runtime YOLO detector adapter."""

from __future__ import annotations

import ast
import json
from typing import Any

import cv2
import numpy as np

from video_analyzer.detectors.base import BaseObjectDetector, Detection
from video_analyzer.detectors.class_map import ClassMapAdapter


class OnnxDetector(BaseObjectDetector):
    """Object detector backed by ONNX Runtime for exported YOLOv8/YOLO11 models."""

    def __init__(
        self,
        model_path: str,
        confidence: float,
        iou: float,
        class_map: ClassMapAdapter,
        input_size: int = 640,
        session: Any = None,
    ) -> None:
        self._model_path = model_path
        self._confidence = confidence
        self._iou = iou
        self._class_map = class_map
        self._input_size = input_size
        self._session = session
        self._input_name: str | None = None
        self._class_names: dict[int, str] = {}

    def provider_info(self) -> dict[str, str]:
        return {
            "provider": "onnx",
            "model": self._model_path,
            "inputSize": str(self._input_size),
        }

    def warmup(self) -> None:
        self._ensure_loaded()

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        session = self._ensure_loaded()
        assert self._input_name is not None

        orig_h, orig_w = frame_bgr.shape[:2]
        blob, scale, pad_x, pad_y = self._preprocess(frame_bgr, self._input_size)
        outputs = session.run(None, {self._input_name: blob})
        return self.decode_outputs(
            outputs[0],
            orig_width=orig_w,
            orig_height=orig_h,
            scale=scale,
            pad_x=pad_x,
            pad_y=pad_y,
        )

    def decode_outputs(
        self,
        raw_output: np.ndarray,
        orig_width: int,
        orig_height: int,
        scale: float,
        pad_x: float,
        pad_y: float,
    ) -> list[Detection]:
        """Decode YOLOv8/v11 raw ONNX predictions or end-to-end NMS outputs."""
        tensor = np.asarray(raw_output)
        if tensor.ndim == 3:
            tensor = tensor[0]
        if tensor.ndim != 2 or tensor.size == 0:
            return []

        # End-to-end ONNX export with built-in NMS: [N, 6] => x1, y1, x2, y2, conf, cls
        if tensor.shape[1] == 6:
            return self._decode_nms_rows(tensor, orig_width, orig_height, scale, pad_x, pad_y)

        # Standard Ultralytics YOLO export: [4 + C, N] -> transpose to [N, 4 + C]
        if tensor.shape[0] < tensor.shape[1]:
            tensor = tensor.T

        if tensor.shape[1] <= 4:
            return []

        boxes_cxcywh = tensor[:, :4]
        class_scores = tensor[:, 4:]
        cls_ids = np.argmax(class_scores, axis=1)
        confidences = class_scores[np.arange(len(class_scores)), cls_ids]

        mask = confidences >= self._confidence
        if not np.any(mask):
            return []

        filtered_boxes = boxes_cxcywh[mask]
        filtered_confs = confidences[mask]
        filtered_cls = cls_ids[mask]

        xywh_for_nms: list[list[float]] = []
        xyxy_unscaled: list[tuple[float, float, float, float]] = []
        for cx, cy, w, h in filtered_boxes:
            x1 = float(cx - w / 2.0 - pad_x) / scale
            y1 = float(cy - h / 2.0 - pad_y) / scale
            x2 = float(cx + w / 2.0 - pad_x) / scale
            y2 = float(cy + h / 2.0 - pad_y) / scale
            x1 = max(0.0, min(float(orig_width), x1))
            y1 = max(0.0, min(float(orig_height), y1))
            x2 = max(0.0, min(float(orig_width), x2))
            y2 = max(0.0, min(float(orig_height), y2))
            xywh_for_nms.append([x1, y1, max(0.0, x2 - x1), max(0.0, y2 - y1)])
            xyxy_unscaled.append((x1, y1, x2, y2))

        by_class: dict[int, list[int]] = {}
        for idx, box in enumerate(xywh_for_nms):
            if box[2] <= 0.0 or box[3] <= 0.0:
                continue
            cls_id = int(filtered_cls[idx])
            by_class.setdefault(cls_id, []).append(idx)

        kept_indices: list[int] = []
        for class_indices in by_class.values():
            class_boxes = [xywh_for_nms[i] for i in class_indices]
            class_confs = [float(filtered_confs[i]) for i in class_indices]
            indices = cv2.dnn.NMSBoxes(
                class_boxes,
                class_confs,
                self._confidence,
                self._iou,
            )
            if len(indices) == 0:
                continue
            for local_idx in np.asarray(indices).reshape(-1).tolist():
                kept_indices.append(class_indices[int(local_idx)])

        if not kept_indices:
            return []

        kept_indices.sort(key=lambda idx: (-float(filtered_confs[idx]), idx))
        detections: list[Detection] = []
        for idx in kept_indices:
            cls_id = int(filtered_cls[idx])
            raw_name = self._class_names.get(cls_id, str(cls_id))
            role = self._class_map.map_class(raw_name, fallback_id=cls_id)
            detections.append(
                Detection(
                    bbox_xyxy=xyxy_unscaled[idx],
                    confidence=float(filtered_confs[idx]),
                    role=role,
                    raw_class_name=raw_name,
                )
            )
        return detections

    def _decode_nms_rows(
        self,
        rows: np.ndarray,
        orig_width: int,
        orig_height: int,
        scale: float,
        pad_x: float,
        pad_y: float,
    ) -> list[Detection]:
        detections: list[Detection] = []
        for x1, y1, x2, y2, conf, cls_val in rows:
            if float(conf) < self._confidence:
                continue
            ux1 = max(0.0, min(float(orig_width), (float(x1) - pad_x) / scale))
            uy1 = max(0.0, min(float(orig_height), (float(y1) - pad_y) / scale))
            ux2 = max(0.0, min(float(orig_width), (float(x2) - pad_x) / scale))
            uy2 = max(0.0, min(float(orig_height), (float(y2) - pad_y) / scale))
            if ux2 <= ux1 or uy2 <= uy1:
                continue
            cls_id = int(cls_val)
            raw_name = self._class_names.get(cls_id, str(cls_id))
            role = self._class_map.map_class(raw_name, fallback_id=cls_id)
            detections.append(
                Detection(
                    bbox_xyxy=(ux1, uy1, ux2, uy2),
                    confidence=float(conf),
                    role=role,
                    raw_class_name=raw_name,
                )
            )
        return detections

    @staticmethod
    def _preprocess(
        frame_bgr: np.ndarray,
        input_size: int,
    ) -> tuple[np.ndarray, float, float, float]:
        height, width = frame_bgr.shape[:2]
        scale = min(input_size / width, input_size / height)
        new_w = int(round(width * scale))
        new_h = int(round(height * scale))
        resized = cv2.resize(frame_bgr, (new_w, new_h), interpolation=cv2.INTER_LINEAR)

        canvas = np.full((input_size, input_size, 3), 114, dtype=np.uint8)
        pad_x = (input_size - new_w) // 2
        pad_y = (input_size - new_h) // 2
        canvas[pad_y : pad_y + new_h, pad_x : pad_x + new_w] = resized

        rgb = cv2.cvtColor(canvas, cv2.COLOR_BGR2RGB)
        normalized = rgb.astype(np.float32) / 255.0
        nchw = np.transpose(normalized, (2, 0, 1))[np.newaxis, ...]
        return nchw, scale, float(pad_x), float(pad_y)

    def _ensure_loaded(self) -> Any:
        if self._session is None:
            try:
                import onnxruntime as ort
            except ImportError as exc:
                raise RuntimeError(
                    "onnxruntime is required for DETECTOR_PROVIDER=onnx. "
                    "Install it with: pip install '.[onnx]'"
                ) from exc
            self._session = ort.InferenceSession(
                self._model_path,
                providers=["CPUExecutionProvider"],
            )
        if self._input_name is None:
            self._input_name = self._session.get_inputs()[0].name
            self._class_names = self._extract_metadata_names(self._session)
        return self._session

    @staticmethod
    def _extract_metadata_names(session: Any) -> dict[int, str]:
        try:
            meta = session.get_modelmeta()
            raw_names = (getattr(meta, "custom_metadata_map", {}) or {}).get("names")
            if not raw_names:
                return {}
            try:
                parsed = json.loads(raw_names)
            except json.JSONDecodeError:
                parsed = ast.literal_eval(raw_names)
            if isinstance(parsed, dict):
                return {int(k): str(v) for k, v in parsed.items()}
        except Exception:
            return {}
        return {}
