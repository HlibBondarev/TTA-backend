"""Gemini Files API client for Pass 1 global video+audio event detection."""

from __future__ import annotations

import json
import logging
import time
from abc import ABC, abstractmethod
from typing import Any

from pydantic import ValidationError

from video_analyzer.config import DEFAULT_GEMINI_MODEL
from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import Pass1ChunkResult
from video_analyzer.pass1.prompt_builder import Pass1PromptBuilder
from video_analyzer.schemas.draft import GeminiTokenUsage, TimeAnchorType
from video_analyzer.schemas.request import MatchSnapshot
from video_analyzer.usage import aggregate_token_usage, extract_usage_metadata

logger = logging.getLogger(__name__)
_REQUIRED_PASS1_KEYS: frozenset[str] = frozenset({"period_events", "time_anchors"})


def _build_pass1_response_schema(types_module: Any) -> Any:
    """Build a default-free Gemini response schema compatible with all google-genai versions."""
    event_schema = types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "time_sec": types_module.Schema(type=types_module.Type.NUMBER),
            "prompt_code": types_module.Schema(type=types_module.Type.STRING),
            "period_number": types_module.Schema(type=types_module.Type.INTEGER),
            "is_lead_to_goal": types_module.Schema(type=types_module.Type.BOOLEAN),
            "actor_bbox_normalized": types_module.Schema(
                type=types_module.Type.ARRAY,
                items=types_module.Schema(type=types_module.Type.NUMBER),
                nullable=True,
            ),
            "pool_zone_hint": types_module.Schema(
                type=types_module.Type.STRING,
                nullable=True,
            ),
            "cap_color_hint": types_module.Schema(
                type=types_module.Type.STRING,
                nullable=True,
            ),
            "confidence": types_module.Schema(type=types_module.Type.NUMBER),
            "evidence": types_module.Schema(
                type=types_module.Type.STRING,
                nullable=True,
            ),
        },
        required=["time_sec", "prompt_code", "period_number", "is_lead_to_goal", "confidence"],
    )
    anchor_schema = types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "time_sec": types_module.Schema(type=types_module.Type.NUMBER),
            "period_number": types_module.Schema(type=types_module.Type.INTEGER),
            "type": types_module.Schema(
                type=types_module.Type.STRING,
                enum=[item.value for item in TimeAnchorType],
            ),
            "confidence": types_module.Schema(type=types_module.Type.NUMBER),
            "evidence": types_module.Schema(
                type=types_module.Type.STRING,
                nullable=True,
            ),
        },
        required=["time_sec", "period_number", "type", "confidence"],
    )
    return types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "period_events": types_module.Schema(
                type=types_module.Type.ARRAY,
                items=event_schema,
            ),
            "time_anchors": types_module.Schema(
                type=types_module.Type.ARRAY,
                items=anchor_schema,
            ),
        },
        required=["period_events", "time_anchors"],
    )


class BasePass1Analyzer(ABC):
    """Interface for Pass 1 global chunk analyzers (facilitates testing and model swaps)."""

    @abstractmethod
    def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
        """Analyze one overlapping chunk and return chunk-relative candidate events and anchors."""


class GeminiPass1Analyzer(BasePass1Analyzer):
    """Pass 1 analyzer using the official google-genai SDK and Gemini Files API."""

    def __init__(
        self,
        api_key: str,
        model_name: str = DEFAULT_GEMINI_MODEL,
        timeout_sec: float = 180.0,
        prompt_builder: Pass1PromptBuilder | None = None,
        client: Any = None,
    ) -> None:
        self._api_key = api_key.strip()
        self._model_name = model_name
        self._timeout_sec = timeout_sec
        self._prompt_builder = prompt_builder or Pass1PromptBuilder()
        self._client = client

    def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
        if not self._api_key and self._client is None:
            raise RuntimeError("GEMINI_API_KEY must be set to run Pass 1 video analysis.")

        from google.genai import types

        client = self._get_client()
        prompt = self._prompt_builder.build_chunk_prompt(snapshot, chunk)

        uploaded_file = client.files.upload(file=str(chunk.path))
        try:
            active_file = self._wait_until_active(client, uploaded_file)
            return self._generate_with_retry(
                client=client,
                uploaded_file=active_file,
                prompt=prompt,
                config=types.GenerateContentConfig(
                    response_mime_type="application/json",
                    response_schema=_build_pass1_response_schema(types),
                    temperature=0.1,
                ),
                chunk_index=chunk.index,
            )
        finally:
            self._safe_delete_file(client, uploaded_file)

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

    def _wait_until_active(self, client: Any, uploaded_file: Any) -> Any:
        deadline = time.monotonic() + self._timeout_sec
        current = uploaded_file
        while True:
            state = getattr(current, "state", None)
            state_name = getattr(state, "name", str(state or "")).upper()
            if state_name in ("", "ACTIVE"):
                return current
            if state_name == "FAILED":
                raise RuntimeError(
                    f"Gemini failed processing uploaded video chunk '{uploaded_file.name}'."
                )
            if time.monotonic() >= deadline:
                raise TimeoutError(
                    f"Timed out waiting for Gemini file '{uploaded_file.name}' to become ACTIVE."
                )
            time.sleep(1.5)
            current = client.files.get(name=uploaded_file.name)

    def _generate_with_retry(
        self,
        client: Any,
        uploaded_file: Any,
        prompt: str,
        config: Any,
        chunk_index: int,
        max_attempts: int = 3,
    ) -> Pass1ChunkResult:
        last_exc: Exception | None = None
        attempt_usages: list[GeminiTokenUsage] = []
        for attempt in range(1, max_attempts + 1):
            try:
                response = client.models.generate_content(
                    model=self._model_name,
                    contents=[uploaded_file, prompt],
                    config=config,
                )
                usage = extract_usage_metadata(response)
                if usage is not None:
                    attempt_usages.append(usage)
                result = self._parse_response(response, chunk_index)
                total_usage = aggregate_token_usage(attempt_usages) if attempt_usages else None
                return result.model_copy(update={"token_usage": total_usage})
            except Exception as exc:
                last_exc = exc
                if attempt == max_attempts:
                    break
                sleep_sec = 2.0**attempt
                logger.warning(
                    "Gemini Pass 1 call attempt %d/%d failed (%s); retrying in %.1fs.",
                    attempt,
                    max_attempts,
                    exc,
                    sleep_sec,
                )
                time.sleep(sleep_sec)
        assert last_exc is not None
        if isinstance(last_exc, RuntimeError):
            raise last_exc
        raise RuntimeError(
            f"Gemini Pass 1 failed for chunk {chunk_index} after {max_attempts} attempts: "
            f"{last_exc}"
        ) from last_exc

    @classmethod
    def _parse_response(cls, response: Any, chunk_index: int) -> Pass1ChunkResult:
        parsed = getattr(response, "parsed", None)
        if isinstance(parsed, Pass1ChunkResult):
            return parsed
        if isinstance(parsed, dict):
            return cls._validate_payload_dict(parsed, chunk_index)

        raw_text = getattr(response, "text", None)
        if not isinstance(raw_text, str) or not raw_text.strip():
            raise RuntimeError(
                f"Gemini Pass 1 returned an empty response for chunk {chunk_index}."
            )
        try:
            payload = json.loads(raw_text)
        except json.JSONDecodeError as exc:
            raise RuntimeError(
                f"Gemini Pass 1 returned invalid JSON for chunk {chunk_index}: {exc}"
            ) from exc
        if not isinstance(payload, dict):
            raise RuntimeError(
                f"Gemini Pass 1 returned a non-object JSON payload for chunk {chunk_index}."
            )
        return cls._validate_payload_dict(payload, chunk_index)

    @staticmethod
    def _validate_payload_dict(payload: dict[str, Any], chunk_index: int) -> Pass1ChunkResult:
        missing_keys = sorted(_REQUIRED_PASS1_KEYS - payload.keys())
        if missing_keys:
            raise RuntimeError(
                f"Gemini Pass 1 response for chunk {chunk_index} is missing required keys: "
                f"{', '.join(missing_keys)}."
            )
        try:
            return Pass1ChunkResult.model_validate(payload)
        except ValidationError as exc:
            raise RuntimeError(
                f"Gemini Pass 1 response failed schema validation for chunk {chunk_index}: {exc}"
            ) from exc

    @staticmethod
    def _safe_delete_file(client: Any, uploaded_file: Any) -> None:
        name = getattr(uploaded_file, "name", None)
        if not name:
            return
        try:
            client.files.delete(name=name)
        except Exception as exc:
            logger.debug("Failed to delete temporary Gemini file '%s': %s", name, exc)
