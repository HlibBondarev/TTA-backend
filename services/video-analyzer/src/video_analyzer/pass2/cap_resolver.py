"""Pluggable cap-number recognition strategies (Gemini 4K crop grid + local OCR fallback)."""

from __future__ import annotations

import logging
import re
from abc import ABC, abstractmethod
from collections import defaultdict
from typing import Any

from pydantic import BaseModel, Field

from video_analyzer.config import Settings
from video_analyzer.pass2.crop_selector import SelectedCrops

logger = logging.getLogger(__name__)


class CapResolutionResult(BaseModel):
    """Consensus cap-number reading returned by a CapResolver."""

    resolved_cap_number: int | None = None
    confidence: float = Field(default=0.0, ge=0.0, le=1.0)
    candidate_scores: dict[int, float] = Field(default_factory=dict)
    reasoning: str = ""


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
    """Default cap resolver sending a native-4K contact sheet to Gemini Vision."""

    def __init__(
        self,
        api_key: str,
        model_name: str = "gemini-2.5-flash",
        client: Any = None,
    ) -> None:
        self._api_key = api_key.strip()
        self._model_name = model_name
        self._client = client

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
        response = client.models.generate_content(
            model=self._model_name,
            contents=[image_part, prompt],
            config=types.GenerateContentConfig(
                response_mime_type="application/json",
                response_schema=_GeminiCapGridResponse,
                temperature=0.0,
            ),
        )

        parsed = getattr(response, "parsed", None)
        if not isinstance(parsed, _GeminiCapGridResponse):
            raw_text = getattr(response, "text", None) or "{}"
            parsed = _GeminiCapGridResponse.model_validate_json(raw_text)

        return self._to_result(parsed, set(allowed_cap_numbers))

    def _get_client(self) -> Any:
        if self._client is None:
            from google import genai

            self._client = genai.Client(api_key=self._api_key)
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
            parsed.consensus_cap_number
            if parsed.consensus_cap_number in allowed_numbers
            else None
        )
        if resolved is not None:
            scores[resolved] = max(scores.get(resolved, 0.0), parsed.confidence)

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
            for _, text, conf in results:
                digits = re.sub(r"\D", "", str(text))
                if not digits:
                    continue
                number = int(digits)
                if number in allowed_set:
                    votes[number].append(float(conf))

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

        best_num, best_score = max(candidate_scores.items(), key=lambda kv: kv[1])
        return CapResolutionResult(
            resolved_cap_number=best_num,
            confidence=best_score,
            candidate_scores=candidate_scores,
            reasoning=(
                f"Local OCR matched cap #{best_num} in {len(votes[best_num])}/{total_crops} crops."
            ),
        )

    def _get_reader(self) -> Any:
        if self._reader is None:
            try:
                import easyocr
            except ImportError as exc:
                raise RuntimeError(
                    "easyocr is required when CAP_RESOLVER_PROVIDER=local_ocr. "
                    "Install it with: pip install '.[ocr]'"
                ) from exc
            self._reader = easyocr.Reader(self._languages, gpu=False, verbose=False)
        return self._reader


class CapResolverFactory:
    """Factory creating the configured CapResolver strategy."""

    @staticmethod
    def create(settings: Settings) -> BaseCapResolver:
        provider = settings.cap_resolver_provider.strip().lower()
        if provider == "gemini_crop_grid":
            return GeminiCropGridCapResolver(
                api_key=settings.gemini_api_key,
                model_name=settings.gemini_pass2_model,
            )
        if provider == "local_ocr":
            return LocalOcrCapResolver(languages=settings.ocr_languages)
        raise ValueError(
            f"Unsupported CAP_RESOLVER_PROVIDER '{settings.cap_resolver_provider}'. "
            "Supported providers: 'gemini_crop_grid', 'local_ocr'."
        )
