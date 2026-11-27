"""Clip-based Gemini verifier: re-checks Pass 1 candidates on short 4 fps clips."""

from __future__ import annotations

import logging
import threading
import time
from collections.abc import Sequence
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from pydantic import ValidationError

from video_analyzer.config import DEFAULT_GEMINI_MODEL
from video_analyzer.gemini.request_config import build_generate_content_config
from video_analyzer.gemini.resilience import (
    GeminiCallBlockedError,
    GeminiResilience,
    GeminiResilienceError,
)
from video_analyzer.media.ffmpeg_client import FFmpegCancelledError, FFmpegClient
from video_analyzer.pass1.models import (
    LabelConflictGroup,
    Pass1AnchorCandidate,
    Pass1EventCandidate,
)
from video_analyzer.pipeline.parallel import ordered_parallel_map
from video_analyzer.schemas.draft import GeminiTokenUsage
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.usage import extract_usage_or_unknown
from video_analyzer.verifier.apply import (
    apply_anchor_verdict,
    apply_event_verdict,
    mark_anchor_unverified,
    mark_event_unverified,
    prune_orphan_stoppage_ends,
    prune_superseded_stoppage_starts,
    settle_conflict_groups,
)
from video_analyzer.verifier.base import BaseEventVerifier
from video_analyzer.verifier.models import (
    VerificationResult,
    VerifierBatchResponse,
    VerifierVerdict,
)
from video_analyzer.verifier.prompt import (
    ClipDescriptor,
    build_clip_header,
    build_verifier_prompt,
    build_verifier_response_schema,
)

logger = logging.getLogger(__name__)

# Asymmetric windows: events are judged mostly by what led up to them, anchors by what follows.
EVENT_PRE_SEC: float = 3.0
EVENT_POST_SEC: float = 2.0
ANCHOR_PRE_SEC: float = 2.0
ANCHOR_POST_SEC: float = 3.0
VERIFIER_VIDEO_FPS: int = 4
VERIFIER_MIME_TYPE: str = "video/mp4"
VERIFIER_CLIP_DIRNAME: str = "verifier_clips"
DEFAULT_MAX_ATTEMPTS: int = 2


@dataclass(slots=True)
class _Item:
    """One candidate (event or anchor) queued for verification."""

    item_id: int
    event: Pass1EventCandidate | None = None
    anchor: Pass1AnchorCandidate | None = None

    @property
    def is_anchor(self) -> bool:
        return self.anchor is not None

    @property
    def time_sec(self) -> float:
        if self.anchor is not None:
            return self.anchor.video_time_sec
        assert self.event is not None
        return self.event.video_time_sec

    @property
    def pre_sec(self) -> float:
        return ANCHOR_PRE_SEC if self.is_anchor else EVENT_PRE_SEC

    @property
    def post_sec(self) -> float:
        return ANCHOR_POST_SEC if self.is_anchor else EVENT_POST_SEC


@dataclass(slots=True)
class _BatchOutcome:
    """Verdicts and accounting of one Gemini batch call (empty when the batch failed open)."""

    verdicts: dict[int, VerifierVerdict] = field(default_factory=dict)
    usages: list[GeminiTokenUsage] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


class GeminiClipVerifier(BaseEventVerifier):
    """Verifies candidates by sending 3-5 short inline video clips per Gemini call.

    Clips are re-encoded from the 4 fps mezzanine and attached as ``video/mp4`` inline parts with
    ``VideoMetadata(fps=4)`` so the model samples every real frame. Every failure mode (open
    breaker, exhausted budget, API or clip errors, missing verdicts) fails open: the candidate is
    kept and flagged for review as ``UNVERIFIED``.
    """

    def __init__(
        self,
        api_key: str,
        ffmpeg_client: FFmpegClient,
        model_name: str = DEFAULT_GEMINI_MODEL,
        timeout_sec: float = 180.0,
        client: Any = None,
        thinking_level: str | None = None,
        resilience: GeminiResilience | None = None,
        max_candidates: int = 80,
        batch_size: int = 4,
        max_concurrency: int = 2,
        max_attempts: int = DEFAULT_MAX_ATTEMPTS,
    ) -> None:
        self._api_key = api_key.strip()
        self._ffmpeg = ffmpeg_client
        self._model_name = model_name
        self._timeout_sec = timeout_sec
        self._client = client
        self._thinking_level = thinking_level
        self._resilience = resilience or GeminiResilience("verifier", model_name)
        self._max_candidates = max(1, max_candidates)
        self._batch_size = max(1, batch_size)
        self._max_concurrency = max(1, max_concurrency)
        self._max_attempts = max(1, max_attempts)

    # ------------------------------------------------------------------ public API

    def verify(
        self,
        context: AnalysisContext,
        events: Sequence[Pass1EventCandidate],
        anchors: Sequence[Pass1AnchorCandidate],
        mezzanine_path: Path,
        work_dir: Path,
        conflict_groups: Sequence[LabelConflictGroup] = (),
        cancel_event: threading.Event | None = None,
    ) -> VerificationResult:
        items = self._build_items(events, anchors)
        selected, skipped = items[: self._max_candidates], items[self._max_candidates :]
        competing = self._competing_labels(conflict_groups)
        blocked = threading.Event()
        batches = [
            selected[offset : offset + self._batch_size]
            for offset in range(0, len(selected), self._batch_size)
        ]

        def run_batch(batch: list[_Item]) -> _BatchOutcome:
            return self._verify_batch(
                context, batch, mezzanine_path, work_dir, competing, blocked, cancel_event
            )

        outcomes = ordered_parallel_map(
            run_batch,
            batches,
            self._max_concurrency,
            before_task=lambda: self._raise_if_cancelled(cancel_event),
        )
        verdicts: dict[int, VerifierVerdict] = {}
        warnings: list[str] = []
        usages: list[GeminiTokenUsage | None] = []
        for outcome in outcomes:
            verdicts.update(outcome.verdicts)
            warnings.extend(outcome.warnings)
            usages.extend(outcome.usages)
        if skipped:
            warnings.append(
                f"Verifier skipped {len(skipped)} candidate(s) above VERIFIER_MAX_CANDIDATES="
                f"{self._max_candidates}; they stay UNVERIFIED."
            )
        return self._apply(context, items, verdicts, conflict_groups, usages, warnings)

    # ------------------------------------------------------------------ batching

    @staticmethod
    def _build_items(
        events: Sequence[Pass1EventCandidate], anchors: Sequence[Pass1AnchorCandidate]
    ) -> list[_Item]:
        """Anchors first (they define dead time), then events, each with a stable item id."""
        items = [_Item(item_id=0, anchor=anchor) for anchor in anchors]
        items += [_Item(item_id=0, event=event) for event in events]
        for index, item in enumerate(items, start=1):
            item.item_id = index
        return items

    @staticmethod
    def _competing_labels(groups: Sequence[LabelConflictGroup]) -> dict[int, tuple[str, ...]]:
        """Map each conflict-group member (by object id) to the labels of its competitors."""
        competing: dict[int, tuple[str, ...]] = {}
        for group in groups:
            for member in group.candidates:
                competing[id(member)] = tuple(
                    other.definition.prompt_code
                    for other in group.candidates
                    if other is not member
                    and other.definition.event_definition_id
                    != member.definition.event_definition_id
                )
        return competing

    @staticmethod
    def _raise_if_cancelled(cancel_event: threading.Event | None) -> None:
        if cancel_event is not None and cancel_event.is_set():
            raise FFmpegCancelledError("Candidate verification cancelled.")

    def _verify_batch(
        self,
        context: AnalysisContext,
        batch: list[_Item],
        mezzanine_path: Path,
        work_dir: Path,
        competing: dict[int, tuple[str, ...]],
        blocked: threading.Event,
        cancel_event: threading.Event | None,
    ) -> _BatchOutcome:
        outcome = _BatchOutcome()
        if blocked.is_set():
            return outcome
        descriptors, clips = self._prepare_clips(
            context, batch, mezzanine_path, work_dir, competing, cancel_event, outcome
        )
        if not descriptors:
            return outcome
        try:
            verdicts = self._request_verdicts(context, descriptors, clips, outcome.usages)
            outcome.verdicts = self._restrict_to_batch(verdicts, descriptors, outcome.warnings)
        except GeminiCallBlockedError as exc:
            blocked.set()
            outcome.warnings.append(
                f"Verifier stopped ({exc.reason.value}): {exc.detail}".rstrip(": ")
            )
        except GeminiResilienceError as exc:
            outcome.warnings.append(f"Verifier batch failed open: {exc}")
        except Exception as exc:
            logger.warning("Verifier batch failed: %s", exc)
            outcome.warnings.append(f"Verifier batch failed open: {exc}")
        return outcome

    @staticmethod
    def _restrict_to_batch(
        verdicts: dict[int, VerifierVerdict],
        descriptors: list[ClipDescriptor],
        warnings: list[str],
    ) -> dict[int, VerifierVerdict]:
        """Keep only verdicts for clips submitted in this batch; reject any other item id.

        A hallucinated or foreign id must never alter a skipped or other-batch candidate. Missing
        verdicts are untouched, so those candidates still fail open as ``UNVERIFIED``.
        """
        submitted = {descriptor.item_id for descriptor in descriptors}
        rejected = sorted(item_id for item_id in verdicts if item_id not in submitted)
        if rejected:
            warnings.append(
                f"Verifier ignored verdict(s) for item id(s) {rejected} "
                "outside the submitted batch."
            )
        return {item_id: v for item_id, v in verdicts.items() if item_id in submitted}

    def _prepare_clips(
        self,
        context: AnalysisContext,
        batch: list[_Item],
        mezzanine_path: Path,
        work_dir: Path,
        competing: dict[int, tuple[str, ...]],
        cancel_event: threading.Event | None,
        outcome: _BatchOutcome,
    ) -> tuple[list[ClipDescriptor], list[bytes]]:
        """Cut one clip per item; items whose clip cannot be cut are left unverified."""
        descriptors: list[ClipDescriptor] = []
        clips: list[bytes] = []
        clip_dir = work_dir / VERIFIER_CLIP_DIRNAME
        for item in batch:
            clip_path = clip_dir / f"clip_{item.item_id:04d}.mp4"
            try:
                self._ffmpeg.cut_verification_clip(
                    mezzanine_path=mezzanine_path,
                    center_sec=item.time_sec,
                    pre_sec=item.pre_sec,
                    post_sec=item.post_sec,
                    out_path=clip_path,
                    cancel_event=cancel_event,
                )
                clips.append(clip_path.read_bytes())
            except FFmpegCancelledError:
                raise
            except Exception as exc:
                outcome.warnings.append(f"Verifier could not cut clip {item.item_id}: {exc}")
                continue
            finally:
                clip_path.unlink(missing_ok=True)
            descriptors.append(self._describe(context, item, competing))
        return descriptors, clips

    @staticmethod
    def _describe(
        context: AnalysisContext, item: _Item, competing: dict[int, tuple[str, ...]]
    ) -> ClipDescriptor:
        start = max(0.0, item.time_sec - item.pre_sec)
        offset = item.time_sec - start
        duration = item.time_sec + item.post_sec - start
        if item.anchor is not None:
            return ClipDescriptor(
                item_id=item.item_id,
                is_anchor=True,
                label=item.anchor.type.value,
                period_number=item.anchor.period_number,
                candidate_offset_sec=offset,
                clip_duration_sec=duration,
            )
        assert item.event is not None
        event = item.event
        alternatives = tuple(
            definition
            for definition in context.event_catalog
            if context.profile.are_confusable(event.definition, definition)
        )
        return ClipDescriptor(
            item_id=item.item_id,
            is_anchor=False,
            label=f"`{event.definition.prompt_code}` ({event.definition.name}, "
            f"{'POSITIVE' if event.definition.is_positive else 'NEGATIVE'})",
            period_number=event.period_number,
            candidate_offset_sec=offset,
            clip_duration_sec=duration,
            alternatives=alternatives,
            competing_labels=competing.get(id(event), ()),
            tracked_attack_direction=event.tracked_attack_direction,
        )

    # ------------------------------------------------------------------ Gemini call

    def _request_verdicts(
        self,
        context: AnalysisContext,
        descriptors: list[ClipDescriptor],
        clips: list[bytes],
        usages: list[GeminiTokenUsage],
    ) -> dict[int, VerifierVerdict]:
        if not self._api_key and self._client is None:
            raise RuntimeError("GEMINI_API_KEY must be set to run the Pass 1 verifier.")

        from google.genai import types

        client = self._get_client()
        contents: list[Any] = []
        for descriptor, data in zip(descriptors, clips, strict=True):
            part = types.Part.from_bytes(data=data, mime_type=VERIFIER_MIME_TYPE)
            part.video_metadata = types.VideoMetadata(fps=VERIFIER_VIDEO_FPS)
            contents.extend([build_clip_header(descriptor), part])
        contents.append(build_verifier_prompt(context, descriptors))
        config = build_generate_content_config(
            types,
            thinking_level=self._thinking_level,
            response_mime_type="application/json",
            response_schema=build_verifier_response_schema(types),
            temperature=0.0,
        )
        return self._generate_with_retry(client, contents, config, usages)

    def _generate_with_retry(
        self,
        client: Any,
        contents: list[Any],
        config: Any,
        usages: list[GeminiTokenUsage],
    ) -> dict[int, VerifierVerdict]:
        last_exc: Exception | None = None
        for attempt in range(1, self._max_attempts + 1):
            response = self._resilience.call(
                lambda: client.models.generate_content(
                    model=self._model_name, contents=contents, config=config
                )
            )
            usages.append(extract_usage_or_unknown(response))
            try:
                return self._parse_response(response)
            except (RuntimeError, ValidationError) as exc:
                last_exc = exc
                logger.warning(
                    "Verifier response attempt %d/%d was malformed: %s",
                    attempt,
                    self._max_attempts,
                    exc,
                )
                time.sleep(self._resilience.backoff_delay(attempt - 1))
        raise RuntimeError(f"Verifier returned malformed output: {last_exc}")

    @staticmethod
    def _parse_response(response: Any) -> dict[int, VerifierVerdict]:
        parsed = getattr(response, "parsed", None)
        if isinstance(parsed, VerifierBatchResponse):
            payload = parsed
        elif isinstance(parsed, dict):
            payload = VerifierBatchResponse.model_validate(parsed)
        else:
            raw_text = getattr(response, "text", None)
            if not isinstance(raw_text, str) or not raw_text.strip():
                raise RuntimeError("Gemini verifier returned an empty response.")
            payload = VerifierBatchResponse.model_validate_json(raw_text)
        return {verdict.item_id: verdict for verdict in payload.verdicts}

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

    # ------------------------------------------------------------------ result assembly

    @staticmethod
    def _apply(
        context: AnalysisContext,
        items: list[_Item],
        verdicts: dict[int, VerifierVerdict],
        conflict_groups: Sequence[LabelConflictGroup],
        usages: list[GeminiTokenUsage | None],
        warnings: list[str],
    ) -> VerificationResult:
        catalog = {definition.prompt_code: definition for definition in context.event_catalog}
        events: list[Pass1EventCandidate] = []
        anchors: list[Pass1AnchorCandidate] = []
        unverified = 0
        for item in items:
            verdict = verdicts.get(item.item_id)
            if verdict is None:
                unverified += 1
            if item.anchor is not None:
                kept_anchor = GeminiClipVerifier._settle_anchor(item.anchor, verdict, warnings)
                if kept_anchor is not None:
                    anchors.append(kept_anchor)
            elif item.event is not None:
                kept_event = GeminiClipVerifier._settle_event(
                    item.event, verdict, catalog, context, warnings
                )
                if kept_event is not None:
                    events.append(kept_event)

        events, settle_warnings = settle_conflict_groups(events, conflict_groups)
        anchors, superseded_warnings = prune_superseded_stoppage_starts(anchors)
        anchors, orphan_warnings = prune_orphan_stoppage_ends(anchors)
        warnings.extend(settle_warnings)
        warnings.extend(superseded_warnings)
        warnings.extend(orphan_warnings)
        if unverified:
            warnings.append(f"Verifier left {unverified} candidate(s) UNVERIFIED (failed open).")
        return VerificationResult(events=events, anchors=anchors, usages=usages, warnings=warnings)

    @staticmethod
    def _settle_event(
        event: Pass1EventCandidate,
        verdict: VerifierVerdict | None,
        catalog: dict[str, Any],
        context: AnalysisContext,
        warnings: list[str],
    ) -> Pass1EventCandidate | None:
        if verdict is None:
            mark_event_unverified(event)
            return event
        kept, warning = apply_event_verdict(
            event, verdict, catalog, context.profile, context.tracked_cap_color_hint
        )
        if warning:
            warnings.append(warning)
        return kept

    @staticmethod
    def _settle_anchor(
        anchor: Pass1AnchorCandidate,
        verdict: VerifierVerdict | None,
        warnings: list[str],
    ) -> Pass1AnchorCandidate | None:
        if verdict is None:
            mark_anchor_unverified(anchor)
            return anchor
        kept, warning = apply_anchor_verdict(anchor, verdict)
        if warning:
            warnings.append(warning)
        return kept
