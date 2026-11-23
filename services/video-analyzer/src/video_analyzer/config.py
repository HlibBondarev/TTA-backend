"""Environment-driven settings for the TTA video analyzer microservice."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Literal

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

DEFAULT_CLASS_MAP: dict[str, str] = {
    "White team": "player",
    "Blue team": "player",
    "Goalkeeper": "player",
    "ball": "ball",
    "Goal": "goal",
    "Referee": "referee",
}


class Settings(BaseSettings):
    """Runtime settings loaded from environment variables and optional .env file."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        case_sensitive=False,
    )

    # Service & security
    service_host: str = Field(default="0.0.0.0")
    service_port: int = Field(default=8000, ge=1, le=65535)
    internal_api_key: str = Field(default="change-me-local-dev-key")
    log_level: str = Field(default="INFO")

    # Gemini API
    gemini_api_key: str = Field(default="")
    gemini_pass1_model: str = Field(default="gemini-2.5-flash")
    gemini_pass2_model: str = Field(default="gemini-2.5-flash")
    gemini_request_timeout_sec: float = Field(default=180.0, gt=0)

    # Pluggable Object Detector
    detector_provider: Literal["roboflow", "local_yolo", "onnx"] = Field(default="roboflow")
    detector_model_path_or_id: str = Field(default="waterpolo-salvasc/5")
    detector_confidence: float = Field(default=0.25, ge=0.0, le=1.0)
    detector_iou: float = Field(default=0.45, ge=0.0, le=1.0)
    detector_device: str = Field(default="auto")
    detector_class_map: dict[str, str] | str = Field(
        default_factory=lambda: dict(DEFAULT_CLASS_MAP)
    )
    detector_onnx_input_size: int = Field(default=640, ge=32)

    # Roboflow-specific settings
    roboflow_api_key: str = Field(default="")
    roboflow_api_url: str = Field(default="https://detect.roboflow.com")
    roboflow_timeout_sec: float = Field(default=30.0, gt=0)

    # Pass 2 Tracking & Cap Number Resolution
    tracker_type: Literal["botsort"] = Field(default="botsort")
    cap_resolver_provider: Literal["gemini_crop_grid", "local_ocr"] = Field(
        default="gemini_crop_grid"
    )
    ocr_languages: list[str] = Field(default_factory=lambda: ["en"])

    # Media binaries & working storage
    ffmpeg_bin: str = Field(default="ffmpeg")
    ffprobe_bin: str = Field(default="ffprobe")
    work_dir: str | None = Field(default=None)

    @field_validator("detector_provider", mode="before")
    @classmethod
    def _normalize_provider(cls, value: Any) -> Any:
        if isinstance(value, str):
            return value.strip().lower()
        return value

    @field_validator("cap_resolver_provider", mode="before")
    @classmethod
    def _normalize_cap_resolver(cls, value: Any) -> Any:
        if isinstance(value, str):
            return value.strip().lower()
        return value

    @field_validator("detector_class_map", mode="before")
    @classmethod
    def _parse_class_map(cls, value: Any) -> dict[str, str]:
        if value is None or value == "":
            return dict(DEFAULT_CLASS_MAP)
        if isinstance(value, dict):
            return {str(k): str(v) for k, v in value.items()}
        if isinstance(value, str):
            stripped = value.strip()
            if not stripped:
                return dict(DEFAULT_CLASS_MAP)
            candidate_path = Path(stripped)
            if not stripped.startswith("{") and candidate_path.is_file():
                loaded = json.loads(candidate_path.read_text(encoding="utf-8"))
                if not isinstance(loaded, dict):
                    raise ValueError("DETECTOR_CLASS_MAP file must contain a JSON object.")
                return {str(k): str(v) for k, v in loaded.items()}
            loaded = json.loads(stripped)
            if not isinstance(loaded, dict):
                raise ValueError("DETECTOR_CLASS_MAP must be a JSON object or path to a JSON file.")
            return {str(k): str(v) for k, v in loaded.items()}
        raise ValueError("Unsupported type for DETECTOR_CLASS_MAP.")

    @property
    def resolved_class_map(self) -> dict[str, str]:
        """Return the normalized class map dictionary."""
        if isinstance(self.detector_class_map, dict):
            return self.detector_class_map
        return self._parse_class_map(self.detector_class_map)
