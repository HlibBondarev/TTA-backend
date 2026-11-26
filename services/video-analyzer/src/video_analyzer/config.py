"""Environment-driven settings for the TTA video analyzer microservice."""

from __future__ import annotations

import ipaddress
import json
import math
from pathlib import Path
from typing import Any, Literal
from urllib.parse import urlsplit

from pydantic import Field, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

INSECURE_PLACEHOLDER_API_KEYS: frozenset[str] = frozenset({"change-me-local-dev-key"})

DEFAULT_GEMINI_MODEL: str = "gemini-4-hydrogen"
DEFAULT_GEMINI_PASS2_MODEL: str = "gemini-3.5-flash-lite"

ThinkingLevelName = Literal["MINIMAL", "LOW", "MEDIUM", "HIGH"]
_UNSET_TEXT_VALUES: frozenset[str] = frozenset({"", "none", "null", "unset", "off"})

# Default estimated USD pricing per 1,000,000 tokens (thinking tokens billed at output rate).
# Override or extend via GEMINI_MODEL_PRICING_USD_PER_1M.
# The gemini-4-neon rates are taken from secondary reports of the public paid-tier price list;
# verify them against the official pricing page and override via the env var if they differ.
DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M: dict[str, dict[str, float]] = {
    "gemini-4-hydrogen": {"input": 0.50, "output": 3.00},
    "gemini-4-neon": {"input": 3.00, "output": 15.00},
    "gemini-3.8-flash": {"input": 0.50, "output": 3.00},
    "gemini-3.6-flash": {"input": 0.30, "output": 2.50},
    "gemini-3.5-flash-lite": {"input": 0.10, "output": 0.40},
}

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
    service_host: str = Field(default="127.0.0.1")
    service_port: int = Field(default=8000, ge=1, le=65535)
    internal_api_key: str = Field(default="")
    callback_api_key: str = Field(default="")
    callback_allowed_origins: list[str] | str = Field(default_factory=list)
    log_level: str = Field(default="INFO")

    # Gemini API
    gemini_api_key: str = Field(default="")
    # Three model slots. The verifier slot is reserved for the Pass 1 verifier clip (#158).
    gemini_pass1_model: str = Field(default=DEFAULT_GEMINI_MODEL)
    gemini_verifier_model: str = Field(default=DEFAULT_GEMINI_MODEL)
    gemini_pass2_model: str = Field(default=DEFAULT_GEMINI_PASS2_MODEL)
    # Per-slot thinking level; unset (None) omits thinking_config from the request entirely.
    gemini_pass1_thinking_level: ThinkingLevelName | None = Field(default="LOW")
    gemini_verifier_thinking_level: ThinkingLevelName | None = Field(default=None)
    gemini_pass2_thinking_level: ThinkingLevelName | None = Field(default=None)
    gemini_request_timeout_sec: float = Field(default=180.0, gt=0)
    gemini_model_pricing_usd_per_1m: dict[str, dict[str, float]] | str = Field(
        default_factory=lambda: _default_gemini_pricing_table()
    )

    # Gemini resilience: per-model RPM pacing, retries, and per-job budget breaker
    gemini_pass1_rpm: int = Field(default=60, ge=1)
    gemini_pass2_rpm: int = Field(default=120, ge=1)
    gemini_max_retries: int = Field(default=3, ge=0, le=10)
    gemini_retry_base_sec: float = Field(default=2.0, gt=0)
    gemini_max_calls_per_job: int | None = Field(default=120, ge=1)
    gemini_max_estimated_cost_usd: float | None = Field(default=3.0, ge=0.0)

    # Pipeline parallelism
    pass1_max_concurrency: int = Field(default=3, ge=1, le=16)
    pass2_phase_a_max_concurrency: int = Field(default=4, ge=1, le=16)
    pass2_phase_b_max_concurrency: int = Field(default=4, ge=1, le=16)

    # Pluggable Object Detector
    detector_provider: Literal["roboflow", "local_yolo", "onnx"] = Field(default="roboflow")
    detector_model_path_or_id: str = Field(default="waterpolo-salvasc/5")
    detector_confidence: float = Field(default=0.25, ge=0.0, le=1.0)
    detector_iou: float = Field(default=0.45, ge=0.0, le=1.0)
    detector_device: str = Field(default="auto")
    detector_class_map: dict[str, str] | str = Field(
        default_factory=lambda: dict(DEFAULT_CLASS_MAP)
    )
    # Global cap on simultaneous remote detector calls across all Phase A tasks.
    detector_max_concurrency: int = Field(default=4, ge=1, le=32)
    # Frames are downscaled to this width for detection; boxes are rescaled to native pixels.
    detector_input_width: int = Field(default=1280, ge=320)
    detector_onnx_input_size: int = Field(default=640, ge=32)
    detector_onnx_output_layout: Literal["auto", "channels_first", "channels_last", "nms"] = Field(
        default="auto"
    )

    # Roboflow-specific settings
    roboflow_api_key: str = Field(default="")
    roboflow_api_url: str = Field(default="https://detect.roboflow.com")
    roboflow_allow_insecure_local_http: bool = Field(default=False)
    roboflow_timeout_sec: float = Field(default=30.0, gt=0)
    roboflow_max_retries: int = Field(default=3, ge=0, le=10)
    roboflow_retry_base_sec: float = Field(default=1.0, gt=0)

    # Pass 2 Tracking & Cap Number Resolution
    tracker_type: Literal["botsort"] = Field(default="botsort")
    cap_resolver_provider: Literal["gemini_crop_grid", "local_ocr"] = Field(
        default="gemini_crop_grid"
    )
    ocr_languages: list[str] = Field(default_factory=lambda: ["en"])

    # Media binaries & working storage
    ffmpeg_bin: str = Field(default="ffmpeg")
    ffprobe_bin: str = Field(default="ffprobe")
    # "auto" lets ffmpeg pick a decoder backend; "none" disables hardware decoding (CI default).
    ffmpeg_hwaccel: str = Field(default="auto")
    # "auto" probes `ffmpeg -encoders` (nvenc -> qsv -> amf -> libx264); or name an encoder.
    ffmpeg_video_encoder: str = Field(default="auto")
    # Cap on simultaneous 4K Pass 2 ffmpeg decoders. Each holds roughly 1-1.5 GB (HEVC 10-bit
    # frame threading), so this bounds peak RAM independently of PASS2_PHASE_A_MAX_CONCURRENCY,
    # which can stay high to overlap remote detector network I/O.
    ffmpeg_extraction_max_concurrency: int = Field(default=2, ge=1, le=16)
    # `-threads` for Pass 2 decoders (0 = ffmpeg default). Fewer threads shrink decoder RAM.
    ffmpeg_extraction_threads: int = Field(default=2, ge=0, le=64)
    # Hardware decode for Pass 2 windows. "none" by default: on the Clip 1 benchmark GPU surface
    # pools cost ~700 MB more RAM per 4K decoder for only ~12% speed. FFMPEG_HWACCEL still
    # applies to the (single, sequential) Pass 1 mezzanine transcode.
    ffmpeg_extraction_hwaccel: str = Field(default="none")
    work_dir: str | None = Field(default=None)

    @field_validator(
        "gemini_pass1_thinking_level",
        "gemini_verifier_thinking_level",
        "gemini_pass2_thinking_level",
        mode="before",
    )
    @classmethod
    def _normalize_thinking_level(cls, value: Any) -> Any:
        if value is None:
            return None
        if isinstance(value, str):
            stripped = value.strip()
            return None if stripped.lower() in _UNSET_TEXT_VALUES else stripped.upper()
        return value

    @field_validator("gemini_max_calls_per_job", "gemini_max_estimated_cost_usd", mode="before")
    @classmethod
    def _empty_budget_means_unlimited(cls, value: Any) -> Any:
        if isinstance(value, str) and value.strip().lower() in _UNSET_TEXT_VALUES:
            return None
        return value

    @field_validator(
        "ffmpeg_hwaccel", "ffmpeg_video_encoder", "ffmpeg_extraction_hwaccel", mode="before"
    )
    @classmethod
    def _normalize_ffmpeg_choice(cls, value: Any) -> Any:
        if isinstance(value, str):
            return value.strip().lower() or "auto"
        return value

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

    @field_validator("detector_onnx_output_layout", mode="before")
    @classmethod
    def _normalize_onnx_output_layout(cls, value: Any) -> Any:
        if isinstance(value, str):
            return value.strip().lower()
        return value

    @field_validator("gemini_model_pricing_usd_per_1m", mode="before")
    @classmethod
    def _parse_gemini_model_pricing(cls, value: Any) -> dict[str, dict[str, float]]:
        return parse_gemini_model_pricing(value)

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

    @field_validator("callback_allowed_origins", mode="before")
    @classmethod
    def _parse_callback_allowed_origins(cls, value: Any) -> list[str]:
        if value is None or value == "":
            return []
        if isinstance(value, str):
            stripped = value.strip()
            if not stripped:
                return []
            if stripped.startswith("["):
                loaded = json.loads(stripped)
                if not isinstance(loaded, list):
                    raise ValueError(
                        "CALLBACK_ALLOWED_ORIGINS JSON value must be a list of origin strings."
                    )
                items = [str(item).strip() for item in loaded if str(item).strip()]
            else:
                items = [part.strip() for part in stripped.split(",") if part.strip()]
        elif isinstance(value, (list, tuple, set)):
            items = [str(item).strip() for item in value if str(item).strip()]
        else:
            raise ValueError("Unsupported type for CALLBACK_ALLOWED_ORIGINS.")

        normalized: list[str] = []
        for item in items:
            origin = normalize_origin(item)
            if origin not in normalized:
                normalized.append(origin)
        return normalized

    @property
    def resolved_gemini_model_pricing(self) -> dict[str, dict[str, float]]:
        """Return the normalized per-1M-token USD pricing table for Gemini models."""
        if isinstance(self.gemini_model_pricing_usd_per_1m, dict):
            return self.gemini_model_pricing_usd_per_1m
        return parse_gemini_model_pricing(self.gemini_model_pricing_usd_per_1m)

    @property
    def resolved_class_map(self) -> dict[str, str]:
        """Return the normalized class map dictionary."""
        if isinstance(self.detector_class_map, dict):
            return self.detector_class_map
        return self._parse_class_map(self.detector_class_map)

    @property
    def resolved_callback_allowed_origins(self) -> tuple[str, ...]:
        """Return the normalized callback origin allowlist."""
        if isinstance(self.callback_allowed_origins, list):
            return tuple(self.callback_allowed_origins)
        return tuple(self._parse_callback_allowed_origins(self.callback_allowed_origins))

    @model_validator(mode="after")
    def _validate_roboflow_url(self) -> Settings:
        self.roboflow_api_url = validate_roboflow_api_url(
            self.roboflow_api_url,
            allow_insecure_local_http=self.roboflow_allow_insecure_local_http,
        )
        return self

    def require_internal_api_key(self) -> str:
        """Validate and return the configured internal API key for server startup."""
        key = self.internal_api_key.strip()
        if not key or key.lower() in INSECURE_PLACEHOLDER_API_KEYS:
            raise ValueError(
                "INTERNAL_API_KEY must be set to a unique non-placeholder secret before "
                "starting the API server."
            )
        return key


def normalize_origin(raw_origin: str) -> str:
    """Validate and canonicalize an HTTP(S) origin string (scheme://host:port)."""
    return _parse_origin(raw_origin, allow_path_and_query=False)


def extract_origin(url: str) -> str:
    """Validate an HTTP(S) callback URL and return its canonical origin (scheme://host:port)."""
    return _parse_origin(url, allow_path_and_query=True)


def validate_roboflow_api_url(
    api_url: str,
    *,
    allow_insecure_local_http: bool = False,
) -> str:
    """Validate Roboflow endpoint URL, requiring HTTPS unless local loopback HTTP is enabled."""
    stripped = api_url.strip()
    if not stripped:
        raise ValueError("ROBOFLOW_API_URL cannot be empty.")

    parsed = urlsplit(stripped)
    scheme = parsed.scheme.lower()
    if parsed.username is not None or parsed.password is not None:
        raise ValueError("ROBOFLOW_API_URL must not contain embedded userinfo credentials.")
    if parsed.query or parsed.fragment:
        raise ValueError("ROBOFLOW_API_URL must not contain query parameters or fragments.")
    hostname = parsed.hostname
    if not hostname:
        raise ValueError(f"ROBOFLOW_API_URL '{api_url}' is missing a valid host.")
    _ = parsed.port

    if scheme == "https":
        return stripped.rstrip("/")
    if scheme == "http":
        if allow_insecure_local_http and _is_loopback_host(hostname):
            return stripped.rstrip("/")
        raise ValueError(
            "ROBOFLOW_API_URL must use https:// "
            "(plain http:// is disallowed unless ROBOFLOW_ALLOW_INSECURE_LOCAL_HTTP=true "
            "for a loopback host)."
        )
    raise ValueError(f"Unsupported ROBOFLOW_API_URL scheme '{parsed.scheme}'; expected https.")


def _is_loopback_host(hostname: str) -> bool:
    normalized = hostname.strip().lower()
    if normalized == "localhost":
        return True
    try:
        return ipaddress.ip_address(normalized).is_loopback
    except ValueError:
        return False


def _parse_origin(raw_url: str, *, allow_path_and_query: bool) -> str:
    stripped = raw_url.strip()
    if not stripped:
        raise ValueError("Origin/URL cannot be empty.")

    parsed = urlsplit(stripped)
    scheme = parsed.scheme.lower()
    if scheme not in {"http", "https"}:
        raise ValueError(f"Unsupported URL scheme '{parsed.scheme}'; expected http or https.")
    if parsed.username is not None or parsed.password is not None:
        raise ValueError("URLs containing embedded userinfo credentials are not allowed.")

    hostname = parsed.hostname
    if not hostname:
        raise ValueError(f"URL '{raw_url}' is missing a valid host.")

    if not allow_path_and_query and (
        parsed.path not in ("", "/") or parsed.query or parsed.fragment
    ):
        raise ValueError(
            f"Allowed callback origin '{raw_url}' must contain only scheme, host, and port."
        )
    if allow_path_and_query and parsed.fragment:
        raise ValueError("Callback URLs must not contain URL fragments.")

    port = parsed.port if parsed.port is not None else (80 if scheme == "http" else 443)
    formatted_host = f"[{hostname}]" if ":" in hostname else hostname
    return f"{scheme}://{formatted_host}:{port}"


def normalize_gemini_model_name(model_name: str) -> str:
    """Normalize a Gemini model identifier by stripping whitespace and optional 'models/' prefix."""
    normalized = model_name.strip().lower()
    if normalized.startswith("models/"):
        normalized = normalized[len("models/") :].strip()
    return normalized


def _default_gemini_pricing_table() -> dict[str, dict[str, float]]:
    return {
        normalize_gemini_model_name(model): {"input": rates["input"], "output": rates["output"]}
        for model, rates in DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M.items()
    }


def _load_pricing_mapping(value: Any) -> dict[str, Any]:
    if isinstance(value, dict):
        return value
    if not isinstance(value, str):
        raise ValueError("Unsupported type for GEMINI_MODEL_PRICING_USD_PER_1M.")
    stripped = value.strip()
    if not stripped:
        return {}
    candidate_path = Path(stripped)
    if not stripped.startswith("{") and candidate_path.is_file():
        loaded = json.loads(candidate_path.read_text(encoding="utf-8"))
    else:
        loaded = json.loads(stripped)
    if not isinstance(loaded, dict):
        raise ValueError(
            "GEMINI_MODEL_PRICING_USD_PER_1M must be a JSON object or path to a JSON file."
        )
    return loaded


def _validate_model_rate_entry(model_key: str, raw_rates: Any) -> dict[str, float]:
    if not isinstance(raw_rates, dict):
        raise ValueError(
            f"Pricing entry for model '{model_key}' must be an object with 'input' and 'output'."
        )
    validated: dict[str, float] = {}
    for rate_key in ("input", "output"):
        raw_val = raw_rates.get(rate_key)
        if isinstance(raw_val, bool) or not isinstance(raw_val, (int, float)):
            raise ValueError(
                f"Pricing entry for model '{model_key}' must include numeric '{rate_key}' rate."
            )
        rate = float(raw_val)
        if not math.isfinite(rate) or rate < 0.0:
            raise ValueError(
                f"Pricing '{rate_key}' rate for model '{model_key}' "
                "must be a finite non-negative number."
            )
        validated[rate_key] = rate
    return validated


def parse_gemini_model_pricing(value: Any) -> dict[str, dict[str, float]]:
    """Parse and merge Gemini per-1M-token USD pricing overrides onto default model rates."""
    merged = _default_gemini_pricing_table()
    if value is None or value == "":
        return merged
    raw_mapping = _load_pricing_mapping(value)
    for raw_model, raw_rates in raw_mapping.items():
        normalized_model = normalize_gemini_model_name(str(raw_model))
        if not normalized_model:
            raise ValueError("GEMINI_MODEL_PRICING_USD_PER_1M cannot contain an empty model name.")
        merged[normalized_model] = _validate_model_rate_entry(normalized_model, raw_rates)
    return merged
