"""Pluggable cap-number recognition strategies (Gemini 4K crop grid + local OCR fallback)."""

from __future__ import annotations

import logging
import re
import threading
from abc import ABC, abstractmethod
from collections import defaultdict
from typing import Any

from pydantic import BaseModel, Field

from video_analyzer.config import DEFAULT_GEMINI_PASS2_MODEL, Settings
from video_analyzer.gemini.request_config import build_generate_content_config
from video_analyzer.gemini.resilience import GeminiResilience
from video_analyzer.pass2.crop_selector import SelectedCrops
from video_analyzer.schemas.draft import GeminiTokenUsage
from video_analyzer.usage import extract_usage_metadata

logger = logging.getLogger(__name__)


def _build_cap_grid_response_schema(types_module: Any) -> Any:
    """Build a default-free Gemini schema for Pass 2 cap-grid recognition."""
    tile_schema = types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "tile_index": types_module.Schema(type=types_module.Type.INTEGER),
            "cap_number": types_module.Schema(
                type=types_module.Type.INTEGER,
                nullable=True,
            ),
            "confidence": types_module.Schema(type=types_module.Type.NUMBER),
        },
        required=["tile_index", "confidence"],
    )
    cand_schema = types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "cap_number": types_module.Schema(type=types_module.Type.INTEGER),
            "score": types_module.Schema(type=types_module.Type.NUMBER),
        },
        required=["cap_number", "score"],
    )
    return types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "tile_readings": types_module.Schema(
                type=types_module.Type.ARRAY,
                items=tile_schema,
            ),
            "consensus_cap_number": types_module.Schema(
                type=types_module.Type.INTEGER,
                nullable=True,
            ),
            "confidence": types_module.Schema(type=types_module.Type.NUMBER),
            "candidate_numbers": types_module.Schema(
                type=types_module.Type.ARRAY,
                items=cand_schema,
            ),
            "reasoning": types_module.Schema(type=types_module.Type.STRING),
        },
        required=[
            "tile_readings",
            "confidence",
            "candidate_numbers",
            "reasoning",
        ],
    )


class CapResolutionError(RuntimeError):
    """Raised when Pass 2 cap resolution fails after receiving a Gemini response."""

    def __init__(
        self,
        message: str,
        *,
        token_usage: GeminiTokenUsage | None = None,
    ) -> None:
        super().__init__(message)
        self.token_usage = token_usage


class CapResolutionResult(BaseModel):
    """Consensus cap-number reading returned by a CapResolver."""

    resolved_cap_number: int | None = None
    confidence: float = Field(default=0.0, ge=0.0, le=1.0)
    candidate_scores: dict[int, float] = Field(default_factory=dict)
    reasoning: str = ""
    token_usage: GeminiTokenUsage | None = Field(default=None, exclude=True)


class _GeminiTileReading(BaseModel):
    tile_index: int
    cap_number: int | None = None
    confidence: float = Field(default=0.0, ge=0.0, le=1.0)


class _GeminiCandidateNumber(BaseModel):
    cap_number: int
    score: float = Field(ge=0.0, le=1.0)


class _GeminiCapGridResponse(BaseModel):
    tile_readings: list[_GeminiTileReading] = Field(default_factory=list)
    consensus_cap_number: int | None = None
    confidence: float = Field(default=0.0, ge=0.0, le=1.0)
    candidate_numbers: list[_GeminiCandidateNumber] = Field(default_factory=list)
    reasoning: str = ""


class BaseCapResolver(ABC):
    """Interface for resolving a player's cap number from selected native-4K crops."""

    @abstractmethod
    def resolve(
        self,
        selected_crops: SelectedCrops,
        allowed_cap_numbers: list[int],
        cap_color_hint: str | None = None,
    ) -> CapResolutionResult:
        """Read cap number(s) from crops constrained to the team lineup roster."""


class GeminiCropGridCapResolver(BaseCapResolver):
    """Default cap resolver sending a native-4K contact sheet to Gemini Vision.

    Requests go through a shared :class:`GeminiResilience` (RPM pacing, classified retries,
    circuit and budget breakers). When a breaker is open, ``resolve`` raises
    ``GeminiCallBlockedError`` without calling the API so callers can fall back or fail fast.
    """

    def __init__(
        self,
        api_key: str,
        model_name: str = DEFAULT_GEMINI_PASS2_MODEL,
        timeout_sec: float = 180.0,
        client: Any = None,
        thinking_level: str | None = None,
        resilience: GeminiResilience | None = None,
    ) -> None:
        self._api_key = api_key.strip()
        self._model_name = model_name
        self._timeout_sec = timeout_sec
        self._client = client
        self._thinking_level = thinking_level
        self._resilience = resilience or GeminiResilience("pass2", model_name)

    def resolve(
        self,
        selected_crops: SelectedCrops,
        allowed_cap_numbers: list[int],
        cap_color_hint: str | None = None,
    ) -> CapResolutionResult:
        if not self._api_key and self._client is None:
            raise RuntimeError(
                "GEMINI_API_KEY must be set when CAP_RESOLVER_PROVIDER=gemini_crop_grid."
            )

        from google.genai import types

        client = self._get_client()
        allowed_str = ", ".join(str(n) for n in sorted(set(allowed_cap_numbers)))
        color_line = f"Expected tracked team cap color: {cap_color_hint}." if cap_color_hint else ""
        prompt = (
            "You are inspecting a contact sheet of cropped Water Polo player head/cap frames "
            "from the SAME player tracklet (#1..#N).\n"
            f"Valid cap numbers on the team lineup roster: [{allowed_str}].\n"
            f"{color_line}\n"
            "Rules:\n"
            "1. Inspect each numbered tile carefully and read any clearly visible cap digit(s).\n"
            "2. NEVER guess if water splash, blur, or angle hides the number: set "
            "`consensus_cap_number` to null and lower `confidence`.\n"
            "3. Only return numbers present in the valid lineup roster list above.\n"
            "4. Provide ranked `candidate_numbers` when partially readable."
        )

        image_part = types.Part.from_bytes(
            data=selected_crops.contact_sheet_jpeg,
            mime_type="image/jpeg",
        )
        config = build_generate_content_config(
            types,
            thinking_level=self._thinking_level,
            response_mime_type="application/json",
            response_schema=_build_cap_grid_response_schema(types),
            temperature=0.0,
        )
        response = self._resilience.call(
            lambda: client.models.generate_content(
                model=self._model_name,
                contents=[image_part, prompt],
                config=config,
            )
        )
        usage = extract_usage_metadata(response)
        try:
            parsed = self._parse_cap_grid_response(response)
            result = self._to_result(parsed, set(allowed_cap_numbers))
        except Exception as exc:
            raise CapResolutionError(str(exc), token_usage=usage) from exc
        return result.model_copy(update={"token_usage": usage})

    @staticmethod
    def _parse_cap_grid_response(response: Any) -> _GeminiCapGridResponse:
        parsed = getattr(response, "parsed", None)
        if isinstance(parsed, _GeminiCapGridResponse):
            return parsed
        if isinstance(parsed, dict):
            return _GeminiCapGridResponse.model_validate(parsed)
        raw_text = getattr(response, "text", None)
        if not isinstance(raw_text, str) or not raw_text.strip():
            raise RuntimeError("Gemini Pass 2 cap resolver returned an empty response.")
        return _GeminiCapGridResponse.model_validate_json(raw_text)

    def _get_client(self) -> Any:
        if self._client is None:
            from google import genai
            from google.genai import types

            timeout_ms = max(1, int(round(self._timeout_sec * 1000.0)))
            self._client = genai.Client(
                api_key=self._api_key,
                http_options=types.HttpOptions(timeout=timeout_ms),
            )
        return self._client

    @staticmethod
    def _to_result(
        parsed: _GeminiCapGridResponse,
        allowed_numbers: set[int],
    ) -> CapResolutionResult:
        scores: dict[int, float] = {}
        for cand in parsed.candidate_numbers:
            if cand.cap_number in allowed_numbers:
                scores[cand.cap_number] = max(scores.get(cand.cap_number, 0.0), cand.score)

        for tile in parsed.tile_readings:
            if tile.cap_number in allowed_numbers:
                scores[tile.cap_number] = max(
                    scores.get(tile.cap_number, 0.0), tile.confidence * 0.9
                )

        resolved = (
            parsed.consensus_cap_number if parsed.consensus_cap_number in allowed_numbers else None
        )
        if resolved is not None:
            if scores:
                pre_top_score = max(scores.values())
                pre_top_candidates = [
                    num for num, score in scores.items() if score == pre_top_score
                ]
                if len(pre_top_candidates) != 1 or pre_top_candidates[0] != resolved:
                    resolved = None
            if resolved is not None:
                scores[resolved] = max(scores.get(resolved, 0.0), parsed.confidence)
                top_score = max(scores.values(), default=0.0)
                top_candidates = [num for num, score in scores.items() if score == top_score]
                if top_score <= 0.0 or len(top_candidates) != 1 or top_candidates[0] != resolved:
                    resolved = None

        resolved_conf = (
            parsed.confidence if resolved is not None else max(scores.values(), default=0.0)
        )
        return CapResolutionResult(
            resolved_cap_number=resolved,
            confidence=resolved_conf,
            candidate_scores=scores,
            reasoning=parsed.reasoning,
        )


class LocalOcrCapResolver(BaseCapResolver):
    """Optional local EasyOCR fallback adapter performing multi-frame digit voting."""

    def __init__(self, languages: list[str] | None = None, reader: Any = None) -> None:
        self._languages = languages or ["en"]
        self._reader = reader
        # Guards lazy reader creation only; ``readtext`` stays unsynchronized by design.
        self._reader_lock = threading.Lock()

    def resolve(
        self,
        selected_crops: SelectedCrops,
        allowed_cap_numbers: list[int],
        cap_color_hint: str | None = None,
    ) -> CapResolutionResult:
        reader = self._get_reader()
        allowed_set = set(allowed_cap_numbers)
        votes: dict[int, list[float]] = defaultdict(list)

        for crop in selected_crops.crops:
            results = reader.readtext(crop.crop_bgr, allowlist="0123456789")
            crop_best: dict[int, float] = {}
            for _, text, conf in results:
                digits = re.sub(r"\D", "", str(text))
                if not digits:
                    continue
                number = int(digits)
                if number in allowed_set:
                    bounded_conf = max(0.0, min(1.0, float(conf)))
                    crop_best[number] = max(crop_best.get(number, 0.0), bounded_conf)
            for number, best_conf in crop_best.items():
                votes[number].append(best_conf)

        if not votes:
            return CapResolutionResult(
                resolved_cap_number=None,
                confidence=0.0,
                candidate_scores={},
                reasoning="Local OCR found no valid roster digits across crops.",
            )

        total_crops = max(1, len(selected_crops.crops))
        candidate_scores: dict[int, float] = {}
        for num, confs in votes.items():
            mean_conf = sum(confs) / len(confs)
            vote_ratio = min(1.0, len(confs) / total_crops)
            candidate_scores[num] = round(min(1.0, mean_conf * 0.7 + vote_ratio * 0.3), 4)

        best_score = max(candidate_scores.values())
        top_candidates = sorted(
            num for num, score in candidate_scores.items() if score == best_score
        )
        if len(top_candidates) > 1:
            tied_labels = ", ".join(f"#{num}" for num in top_candidates)
            return CapResolutionResult(
                resolved_cap_number=None,
                confidence=best_score,
                candidate_scores=candidate_scores,
                reasoning=(
                    f"Local OCR tied at {best_score:.4f} across caps [{tied_labels}]; "
                    "leaving unresolved for review."
                ),
            )

        best_num = top_candidates[0]
        return CapResolutionResult(
            resolved_cap_number=best_num,
            confidence=best_score,
            candidate_scores=candidate_scores,
            reasoning=(
                f"Local OCR matched cap #{best_num} in {len(votes[best_num])}/{total_crops} crops."
            ),
        )

    def _get_reader(self) -> Any:
        if self._reader is not None:
            return self._reader
        with self._reader_lock:
            if self._reader is None:
                self._reader = self._create_reader()
        return self._reader

    def _create_reader(self) -> Any:
        try:
            import easyocr
        except ImportError as exc:
            raise RuntimeError(
                "easyocr is required when CAP_RESOLVER_PROVIDER=local_ocr. "
                "Install it with: pip install '.[ocr]'"
            ) from exc
        return easyocr.Reader(self._languages, gpu=False, verbose=False)


class CapResolverFactory:
    """Factory creating the configured CapResolver strategy."""

    @staticmethod
    def create(
        settings: Settings,
        resilience: GeminiResilience | None = None,
    ) -> BaseCapResolver:
        provider = settings.cap_resolver_provider.strip().lower()
        if provider == "gemini_crop_grid":
            return GeminiCropGridCapResolver(
                api_key=settings.gemini_api_key,
                model_name=settings.gemini_pass2_model,
                timeout_sec=settings.gemini_request_timeout_sec,
                thinking_level=settings.gemini_pass2_thinking_level,
                resilience=resilience or GeminiResilience.from_settings(settings, "pass2"),
            )
        if provider == "local_ocr":
            return LocalOcrCapResolver(languages=settings.ocr_languages)
        raise ValueError(
            f"Unsupported CAP_RESOLVER_PROVIDER '{settings.cap_resolver_provider}'. "
            "Supported providers: 'gemini_crop_grid', 'local_ocr'."
        )
