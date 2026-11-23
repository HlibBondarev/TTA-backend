"""Gemini Files API client for Pass 1 global video+audio event detection."""

from __future__ import annotations

import logging
import time
from abc import ABC, abstractmethod
from typing import Any

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.models import Pass1ChunkResult
from video_analyzer.pass1.prompt_builder import Pass1PromptBuilder
from video_analyzer.schemas.request import MatchSnapshot

logger = logging.getLogger(__name__)


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
        model_name: str = "gemini-2.5-flash",
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
            response = self._generate_with_retry(
                client=client,
                uploaded_file=active_file,
                prompt=prompt,
                config=types.GenerateContentConfig(
                    response_mime_type="application/json",
                    response_schema=Pass1ChunkResult,
                    temperature=0.1,
                ),
            )
            parsed = getattr(response, "parsed", None)
            if isinstance(parsed, Pass1ChunkResult):
                return parsed
            raw_text = getattr(response, "text", None) or "{}"
            return Pass1ChunkResult.model_validate_json(raw_text)
        finally:
            self._safe_delete_file(client, uploaded_file)

    def _get_client(self) -> Any:
        if self._client is None:
            from google import genai

            self._client = genai.Client(api_key=self._api_key)
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
        max_attempts: int = 3,
    ) -> Any:
        last_exc: Exception | None = None
        for attempt in range(1, max_attempts + 1):
            try:
                return client.models.generate_content(
                    model=self._model_name,
                    contents=[uploaded_file, prompt],
                    config=config,
                )
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
        raise last_exc

    @staticmethod
    def _safe_delete_file(client: Any, uploaded_file: Any) -> None:
        name = getattr(uploaded_file, "name", None)
        if not name:
            return
        try:
            client.files.delete(name=name)
        except Exception as exc:
            logger.debug("Failed to delete temporary Gemini file '%s': %s", name, exc)
