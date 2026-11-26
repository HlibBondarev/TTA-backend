"""ONNX Runtime YOLO detector adapter."""

from __future__ import annotations

import ast
import json
import threading
from typing import Any, Literal

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
        output_layout: Literal["auto", "channels_first", "channels_last", "nms"] = "auto",
        session: Any = None,
    ) -> None:
        self._model_path = model_path
        self._confidence = confidence
        self._iou = iou
        self._class_map = class_map
        self._input_size = input_size
        self._output_layout = output_layout
        self._session = session
        self._load_lock = threading.Lock()
        self._input_name: str | None = None
        self._output_shape: tuple[Any, ...] | None = None
        self._class_names: dict[int, str] = {}

    def provider_info(self) -> dict[str, str]:
        return {
            "provider": "onnx",
            "model": self._model_path,
            "inputSize": str(self._input_size),
            "outputLayout": self._output_layout,
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
        if self._is_nms_output(tensor):
            return self._decode_nms_rows(tensor, orig_width, orig_height, scale, pad_x, pad_y)

        # Normalize Ultralytics raw YOLO exports to [N, 4 + C]
        if self._should_transpose_yolo_tensor(tensor):
            tensor = tensor.T

        if tensor.shape[1] <= 4:
            return []

        class_scores = tensor[:, 4:]
        cls_ids = np.argmax(class_scores, axis=1)
        confidences = class_scores[np.arange(len(class_scores)), cls_ids]

        mask = confidences >= self._confidence
        if not np.any(mask):
            return []

        filtered_confs = confidences[mask]
        filtered_cls = cls_ids[mask]
        xywh_for_nms, xyxy_unscaled = self._project_boxes(
            tensor[:, :4][mask], orig_width, orig_height, scale, pad_x, pad_y
        )
        kept_indices = self._nms_per_class(xywh_for_nms, filtered_confs, filtered_cls)
        kept_indices.sort(key=lambda idx: (-float(filtered_confs[idx]), idx))
        return [
            self._build_detection(
                xyxy_unscaled[idx], float(filtered_confs[idx]), int(filtered_cls[idx])
            )
            for idx in kept_indices
        ]

    @staticmethod
    def _project_boxes(
        boxes_cxcywh: np.ndarray,
        orig_width: int,
        orig_height: int,
        scale: float,
        pad_x: float,
        pad_y: float,
    ) -> tuple[list[list[float]], list[tuple[float, float, float, float]]]:
        """Map letterboxed center-format boxes to clamped native-pixel xywh and xyxy lists."""
        xywh: list[list[float]] = []
        xyxy: list[tuple[float, float, float, float]] = []
        for cx, cy, w, h in boxes_cxcywh:
            x1 = float(cx - w / 2.0 - pad_x) / scale
            y1 = float(cy - h / 2.0 - pad_y) / scale
            x2 = float(cx + w / 2.0 - pad_x) / scale
            y2 = float(cy + h / 2.0 - pad_y) / scale
            x1 = max(0.0, min(float(orig_width), x1))
            y1 = max(0.0, min(float(orig_height), y1))
            x2 = max(0.0, min(float(orig_width), x2))
            y2 = max(0.0, min(float(orig_height), y2))
            xywh.append([x1, y1, max(0.0, x2 - x1), max(0.0, y2 - y1)])
            xyxy.append((x1, y1, x2, y2))
        return xywh, xyxy

    def _nms_per_class(
        self,
        xywh_boxes: list[list[float]],
        confidences: np.ndarray,
        cls_ids: np.ndarray,
    ) -> list[int]:
        """Run class-wise NMS over non-degenerate boxes and return the surviving indices."""
        by_class: dict[int, list[int]] = {}
        for idx, box in enumerate(xywh_boxes):
            if box[2] > 0.0 and box[3] > 0.0:
                by_class.setdefault(int(cls_ids[idx]), []).append(idx)

        kept_indices: list[int] = []
        for class_indices in by_class.values():
            class_boxes = [xywh_boxes[i] for i in class_indices]
            class_confs = [float(confidences[i]) for i in class_indices]
            indices = cv2.dnn.NMSBoxes(class_boxes, class_confs, self._confidence, self._iou)
            local_indices = np.asarray(indices).reshape(-1).tolist() if len(indices) else []
            kept_indices.extend(class_indices[int(local_idx)] for local_idx in local_indices)
        return kept_indices

    def _build_detection(
        self,
        bbox_xyxy: tuple[float, float, float, float],
        confidence: float,
        cls_id: int,
    ) -> Detection:
        raw_name = self._class_names.get(cls_id, str(cls_id))
        role = self._class_map.map_class(raw_name, fallback_id=cls_id)
        return Detection(
            bbox_xyxy=bbox_xyxy,
            confidence=confidence,
            role=role,
            raw_class_name=raw_name,
        )

    def _is_nms_output(self, tensor: np.ndarray) -> bool:
        if self._output_layout == "nms":
            return tensor.shape[1] == 6
        if self._output_layout in ("channels_first", "channels_last") or tensor.shape[1] != 6:
            return False
        if tensor.shape[0] != 6:
            return True
        # Disambiguate equal-axis (6, 6): NMS rows have conf in [0, 1], integer cls, and x2 >= x1
        confs = tensor[:, 4]
        clss = tensor[:, 5]
        return bool(
            np.all(confs >= 0.0)
            and np.all(confs <= 1.0)
            and np.all(clss >= 0.0)
            and np.all(np.isclose(clss, np.round(clss)))
            and np.all(tensor[:, 2] >= tensor[:, 0])
            and np.all(tensor[:, 3] >= tensor[:, 1])
        )

    def _should_transpose_yolo_tensor(self, tensor: np.ndarray) -> bool:
        if self._output_layout == "channels_first":
            return True
        if self._output_layout == "channels_last":
            return False

        hint = self._transpose_hint_from_class_count(tensor)
        if hint is None:
            hint = self._transpose_hint_from_declared_shape()
        if hint is None:
            hint = self._transpose_hint_from_score_ranges(tensor)
        if hint is not None:
            return hint

        if tensor.shape[0] != tensor.shape[1]:
            return tensor.shape[0] < tensor.shape[1]
        return True

    def _transpose_hint_from_class_count(self, tensor: np.ndarray) -> bool | None:
        """Use the model's class count: exactly one axis equal to ``4 + classes`` is channels."""
        if not self._class_names:
            return None
        expected_channels = 4 + len(self._class_names)
        matches_axis0 = tensor.shape[0] == expected_channels
        matches_axis1 = tensor.shape[1] == expected_channels
        return _exclusive_axis_hint(matches_axis0, matches_axis1)

    def _transpose_hint_from_declared_shape(self) -> bool | None:
        """Use the ONNX-declared shape: the static axis is channels, the dynamic one is anchors."""
        if self._output_shape is None or len(self._output_shape) < 2:
            return None
        s0, s1 = self._output_shape[-2], self._output_shape[-1]
        return _exclusive_axis_hint(isinstance(s0, int), isinstance(s1, int))

    @staticmethod
    def _transpose_hint_from_score_ranges(tensor: np.ndarray) -> bool | None:
        """Prefer the orientation whose class-score slice looks like probabilities."""
        if tensor.shape[0] <= 4 or tensor.shape[1] <= 4:
            return None
        channels_first_valid = _is_probability_slice(tensor[4:, :])
        channels_last_valid = _is_probability_slice(tensor[:, 4:])
        return _exclusive_axis_hint(channels_first_valid, channels_last_valid)

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
        if self._session is not None and self._input_name is not None:
            return self._session
        with self._load_lock:
            session = self._session
            if session is None:
                try:
                    import onnxruntime as ort
                except ImportError as exc:
                    raise RuntimeError(
                        "onnxruntime is required for DETECTOR_PROVIDER=onnx. "
                        "Install it with: pip install '.[onnx]'"
                    ) from exc
                session = ort.InferenceSession(
                    self._model_path,
                    providers=["CPUExecutionProvider"],
                )
            if self._input_name is None:
                input_name = session.get_inputs()[0].name
                outputs = session.get_outputs()
                output_shape: tuple[Any, ...] | None = None
                if outputs:
                    raw_shape = getattr(outputs[0], "shape", None)
                    if raw_shape is not None:
                        output_shape = tuple(raw_shape)
                class_names = self._extract_metadata_names(session)
                self._output_shape = output_shape
                self._class_names = class_names
                self._session = session
                # Publish _input_name last so fast-path callers never see partial init state.
                self._input_name = input_name
            elif self._session is None:
                self._session = session
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


def _exclusive_axis_hint(first_axis_matches: bool, second_axis_matches: bool) -> bool | None:
    """Return True/False when exactly the first/second axis matches, else None (ambiguous)."""
    if first_axis_matches and not second_axis_matches:
        return True
    if second_axis_matches and not first_axis_matches:
        return False
    return None


def _is_probability_slice(values: np.ndarray) -> bool:
    return bool(
        np.all(np.isfinite(values)) and np.all(values >= -1e-3) and np.all(values <= 1.0 + 1e-3)
    )
