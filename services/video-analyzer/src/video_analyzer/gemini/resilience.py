"""Shared Gemini resilience layer: pacing, classified retries, and circuit/budget breakers.

One :class:`GeminiResilience` instance guards one model slot (Pass 1 or Pass 2):

* a per-model RPM pacer spaces calls so the quota is not exceeded,
* transient failures (500/502/503/504, timeouts) and short per-minute 429s are retried with
  jittered exponential backoff, honoring the server's ``retryDelay``,
* non-retryable failures (400/401/403, daily ``PerDay`` 429, ``retryDelay`` > 60 s) are never
  retried and open a circuit breaker so the rest of the job stops calling Gemini,
* a per-job budget breaker enforces ``GEMINI_MAX_CALLS_PER_JOB`` and
  ``GEMINI_MAX_ESTIMATED_COST_USD``.

Breaker and budget state is job-scoped (see :class:`GeminiJobScope`) because one orchestrator
serves several concurrent jobs, whereas the RPM pacer is process-wide because quotas are too.
"""

from __future__ import annotations

import json
import logging
import random
import re
import threading
import time
from collections.abc import Callable, Iterator, Mapping
from contextlib import contextmanager
from contextvars import ContextVar
from dataclasses import dataclass
from enum import StrEnum
from typing import Any, TypeVar

import httpx

from video_analyzer.config import Settings, parse_gemini_model_pricing
from video_analyzer.usage import estimate_call_cost_usd, extract_usage_metadata

logger = logging.getLogger(__name__)

T = TypeVar("T")

SHORT_RETRY_DELAY_LIMIT_SEC: float = 60.0
MAX_BACKOFF_SEC: float = 60.0
_MAX_DETAIL_CHARS: int = 300
_TRANSIENT_STATUS_CODES: frozenset[int] = frozenset({408, 500, 502, 503, 504})
_HTTP_TOO_MANY_REQUESTS: int = 429
_DAILY_QUOTA_MARKER: str = "perday"
_RETRY_DELAY_TEXT_PATTERN = re.compile(
    r"retry(?:Delay)?\W{0,6}(?:in\W{0,3})?(\d+(?:\.\d+)?)\s*(ms|s)\b", re.IGNORECASE
)


class BreakerReason(StrEnum):
    """Why a circuit breaker opened."""

    QUOTA_EXHAUSTED = "quota_exhausted"
    API_ERROR = "api_error"
    BUDGET_EXCEEDED = "budget_exceeded"


class GeminiErrorKind(StrEnum):
    """Retry class assigned to a Gemini failure."""

    TRANSIENT = "transient"
    RATE_LIMITED = "rate_limited"
    QUOTA_EXHAUSTED = "quota_exhausted"
    PERMANENT = "permanent"
    UNKNOWN = "unknown"


class GeminiResilienceError(RuntimeError):
    """Base class for failures raised by the resilience layer."""


class GeminiCallBlockedError(GeminiResilienceError):
    """Raised when a call is refused or fails non-retryably and a breaker is (now) open."""

    def __init__(self, slot: str, reason: BreakerReason, detail: str = "") -> None:
        self.slot = slot
        self.reason = reason
        self.detail = detail
        suffix = f": {detail}" if detail else ""
        super().__init__(f"Gemini {slot} calls blocked ({reason.value}){suffix}")


class GeminiRetriesExhaustedError(GeminiResilienceError):
    """Raised when a retriable Gemini failure persisted through every allowed retry."""


@dataclass(frozen=True, slots=True)
class ErrorClassification:
    """Retry decision derived from one Gemini exception."""

    kind: GeminiErrorKind
    retry_after_sec: float | None = None
    status_code: int | None = None


def _status_code_of(exc: BaseException) -> int | None:
    for attr in ("code", "status_code"):
        value = getattr(exc, attr, None)
        if isinstance(value, int) and not isinstance(value, bool):
            return value
    response_status = getattr(getattr(exc, "response", None), "status_code", None)
    if isinstance(response_status, int) and not isinstance(response_status, bool):
        return response_status
    return None


def _error_text(exc: BaseException) -> str:
    parts = [str(exc), str(getattr(exc, "status", "") or "")]
    details = getattr(exc, "details", None)
    if details is not None:
        try:
            parts.append(json.dumps(details, default=str))
        except (TypeError, ValueError):
            parts.append(str(details))
    return " ".join(parts)


def _find_key(payload: Any, key: str) -> Any:
    if isinstance(payload, Mapping):
        if key in payload:
            return payload[key]
        return next((hit for v in payload.values() if (hit := _find_key(v, key)) is not None), None)
    if isinstance(payload, list):
        return next((hit for v in payload if (hit := _find_key(v, key)) is not None), None)
    return None


def _parse_delay_value(raw: Any) -> float | None:
    if isinstance(raw, Mapping):
        seconds = raw.get("seconds", 0)
        nanos = raw.get("nanos", 0)
        if isinstance(seconds, (int, float)) and isinstance(nanos, (int, float)):
            return float(seconds) + float(nanos) / 1_000_000_000.0
        return None
    if isinstance(raw, (int, float)) and not isinstance(raw, bool):
        return float(raw)
    if isinstance(raw, str):
        match = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*(ms|s)?\s*", raw)
        if match:
            return float(match.group(1)) / (1000.0 if match.group(2) == "ms" else 1.0)
    return None


def extract_retry_delay_sec(exc: BaseException) -> float | None:
    """Extract the server-suggested retry delay (seconds) from a Gemini 429 error, if present."""
    structured = _parse_delay_value(_find_key(getattr(exc, "details", None), "retryDelay"))
    if structured is not None:
        return structured
    match = _RETRY_DELAY_TEXT_PATTERN.search(_error_text(exc))
    if match is None:
        return None
    return float(match.group(1)) / (1000.0 if match.group(2).lower() == "ms" else 1.0)


def _classify_rate_limit(exc: BaseException) -> ErrorClassification:
    delay = extract_retry_delay_sec(exc)
    is_daily = _DAILY_QUOTA_MARKER in _error_text(exc).lower()
    if is_daily or (delay is not None and delay > SHORT_RETRY_DELAY_LIMIT_SEC):
        return ErrorClassification(GeminiErrorKind.QUOTA_EXHAUSTED, delay, _HTTP_TOO_MANY_REQUESTS)
    return ErrorClassification(GeminiErrorKind.RATE_LIMITED, delay, _HTTP_TOO_MANY_REQUESTS)


def classify_gemini_error(exc: BaseException) -> ErrorClassification:
    """Map a Gemini/HTTP exception to a retry class.

    * 429 with a daily (``PerDay``) quota or ``retryDelay`` > 60 s -> ``QUOTA_EXHAUSTED``
    * other 429 -> ``RATE_LIMITED`` (retry honoring ``retryDelay``)
    * 500/502/503/504/408, timeouts and transport errors -> ``TRANSIENT``
    * any other HTTP status (400, 401, 403, 404, ...) -> ``PERMANENT``
    * exceptions that carry no HTTP status -> ``UNKNOWN`` (never retried here)
    """
    status = _status_code_of(exc)
    if status == _HTTP_TOO_MANY_REQUESTS:
        return _classify_rate_limit(exc)
    if status is not None:
        kind = (
            GeminiErrorKind.TRANSIENT
            if status in _TRANSIENT_STATUS_CODES
            else GeminiErrorKind.PERMANENT
        )
        return ErrorClassification(kind, None, status)
    if isinstance(exc, (TimeoutError, ConnectionError, httpx.TransportError)):
        return ErrorClassification(GeminiErrorKind.TRANSIENT)
    return ErrorClassification(GeminiErrorKind.UNKNOWN)


class RpmPacer:
    """Per-model requests-per-minute token bucket with a burst of one token.

    Calls are spaced ``60 / rpm`` seconds apart. Concurrent callers reserve consecutive slots
    under a lock and then sleep outside it, so many threads queue fairly without busy waiting.
    """

    def __init__(
        self,
        rpm: int,
        *,
        clock: Callable[[], float] = time.monotonic,
        sleep: Callable[[float], None] = time.sleep,
    ) -> None:
        if rpm < 1:
            raise ValueError("rpm must be at least 1.")
        self._interval_sec = 60.0 / rpm
        self._clock = clock
        self._sleep = sleep
        self._lock = threading.Lock()
        self._next_slot = 0.0

    def acquire(self) -> float:
        """Block until a call slot is available and return the seconds waited."""
        with self._lock:
            now = self._clock()
            slot = max(now, self._next_slot)
            self._next_slot = slot + self._interval_sec
        wait_sec = slot - now
        if wait_sec > 0.0:
            self._sleep(wait_sec)
        return max(0.0, wait_sec)


class CircuitBreaker:
    """Thread-safe latch that, once opened, refuses further calls for the rest of the job."""

    def __init__(self, name: str) -> None:
        self.name = name
        self._lock = threading.Lock()
        self._reason: BreakerReason | None = None
        self._detail = ""

    @property
    def is_open(self) -> bool:
        return self._reason is not None

    @property
    def reason(self) -> BreakerReason | None:
        return self._reason

    @property
    def detail(self) -> str:
        return self._detail

    def trip(self, reason: BreakerReason, detail: str = "") -> bool:
        """Open the breaker; return True only for the call that actually opened it."""
        with self._lock:
            if self._reason is not None:
                return False
            self._reason = reason
            self._detail = detail[:_MAX_DETAIL_CHARS]
        logger.warning("Gemini circuit breaker '%s' opened (%s): %s", self.name, reason, detail)
        return True

    def raise_if_open(self) -> None:
        reason = self._reason
        if reason is not None:
            raise GeminiCallBlockedError(self.name, reason, self._detail)


class JobBudget:
    """Per-job Gemini call-count and estimated-cost ceilings (``None`` means unlimited)."""

    def __init__(self, max_calls: int | None, max_cost_usd: float | None) -> None:
        self.max_calls = max_calls
        self.max_cost_usd = max_cost_usd
        self._lock = threading.Lock()
        self._calls = 0
        self._cost_usd = 0.0

    @property
    def calls(self) -> int:
        return self._calls

    @property
    def estimated_cost_usd(self) -> float:
        return self._cost_usd

    def try_reserve_call(self) -> str | None:
        """Reserve one call; return a description of the exceeded limit, or None when allowed."""
        with self._lock:
            if self.max_cost_usd is not None and self._cost_usd >= self.max_cost_usd:
                return (
                    f"estimated cost ${self._cost_usd:.4f} reached the "
                    f"GEMINI_MAX_ESTIMATED_COST_USD limit of ${self.max_cost_usd:.2f}"
                )
            if self.max_calls is not None and self._calls >= self.max_calls:
                return f"{self._calls} calls reached the GEMINI_MAX_CALLS_PER_JOB limit"
            self._calls += 1
            return None

    def add_cost(self, cost_usd: float) -> None:
        with self._lock:
            self._cost_usd += max(0.0, cost_usd)


class GeminiJobScope:
    """Job-scoped breaker and budget state shared by every Gemini slot during one job."""

    def __init__(
        self,
        max_calls: int | None = None,
        max_cost_usd: float | None = None,
    ) -> None:
        self.budget = JobBudget(max_calls, max_cost_usd)
        self.budget_breaker = CircuitBreaker("budget")
        self._breakers: dict[str, CircuitBreaker] = {}
        self._lock = threading.Lock()

    def breaker_for(self, slot: str) -> CircuitBreaker:
        with self._lock:
            return self._breakers.setdefault(slot, CircuitBreaker(slot))

    def open_breakers(self) -> list[CircuitBreaker]:
        """Return every breaker (slot breakers and the budget breaker) that is currently open."""
        with self._lock:
            candidates = [*self._breakers.values(), self.budget_breaker]
        return [breaker for breaker in candidates if breaker.is_open]


_CURRENT_SCOPE: ContextVar[GeminiJobScope | None] = ContextVar("gemini_job_scope", default=None)


def current_job_scope() -> GeminiJobScope | None:
    """Return the job scope bound to the current execution context, if any."""
    return _CURRENT_SCOPE.get()


@contextmanager
def job_scope(scope: GeminiJobScope) -> Iterator[GeminiJobScope]:
    """Bind ``scope`` for the duration of a job so all Gemini calls share its breakers/budget."""
    token = _CURRENT_SCOPE.set(scope)
    try:
        yield scope
    finally:
        _CURRENT_SCOPE.reset(token)


class GeminiResilience:
    """Wraps Gemini API calls of one model slot with pacing, retries, and breakers."""

    def __init__(
        self,
        slot: str,
        model: str,
        *,
        rpm: int | None = None,
        max_retries: int = 3,
        retry_base_sec: float = 2.0,
        pricing_table: Mapping[str, Mapping[str, float]] | None = None,
        sleep: Callable[[float], None] = time.sleep,
        rng: Callable[[], float] = random.random,
        pacer: RpmPacer | None = None,
    ) -> None:
        if max_retries < 0:
            raise ValueError("max_retries must be non-negative.")
        if retry_base_sec <= 0:
            raise ValueError("retry_base_sec must be positive.")
        self.slot = slot
        self.model = model
        self._max_retries = max_retries
        self._retry_base_sec = retry_base_sec
        self._pricing_table = pricing_table or parse_gemini_model_pricing(None)
        self._sleep = sleep
        self._rng = rng
        self._pacer = pacer or (RpmPacer(rpm, sleep=sleep) if rpm else None)
        self._fallback_scope = GeminiJobScope()

    @classmethod
    def from_settings(cls, settings: Settings, slot: str) -> GeminiResilience:
        """Build the resilience guard for ``slot`` (``pass1``, ``verifier`` or ``pass2``)."""
        models = {
            "pass1": (settings.gemini_pass1_model, settings.gemini_pass1_rpm),
            "verifier": (settings.gemini_verifier_model, settings.gemini_pass1_rpm),
            "pass2": (settings.gemini_pass2_model, settings.gemini_pass2_rpm),
        }
        if slot not in models:
            raise ValueError(f"Unknown Gemini slot '{slot}'. Expected one of {sorted(models)}.")
        model, rpm = models[slot]
        return cls(
            slot,
            model,
            rpm=rpm,
            max_retries=settings.gemini_max_retries,
            retry_base_sec=settings.gemini_retry_base_sec,
            pricing_table=settings.resolved_gemini_model_pricing,
        )

    def backoff_delay(self, attempt: int, retry_after_sec: float | None = None) -> float:
        """Return the jittered delay before retry number ``attempt`` (0-based).

        A server-provided ``retry_after_sec`` is honored as a lower bound plus a little jitter;
        otherwise the delay grows exponentially from ``retry_base_sec`` with 50-100% jitter.
        """
        if retry_after_sec is not None:
            return max(0.0, retry_after_sec) + self._rng() * min(1.0, self._retry_base_sec)
        exponential = min(MAX_BACKOFF_SEC, self._retry_base_sec * (2.0**attempt))
        return exponential * (0.5 + 0.5 * self._rng())

    def call(self, fn: Callable[[], T], *, paced: bool = True, counted: bool = True) -> T:
        """Run ``fn`` (one Gemini API request) under pacing, retry, and breaker policies.

        Args:
            fn: Zero-argument callable performing a single API request.
            paced: Apply the per-model RPM pacer before each attempt.
            counted: Count each attempt (and the response cost) against the job budget.

        Raises:
            GeminiCallBlockedError: A breaker is open or the failure was non-retryable.
            GeminiRetriesExhaustedError: A retriable failure persisted through all retries.
        """
        scope = current_job_scope() or self._fallback_scope
        breaker = scope.breaker_for(self.slot)
        attempt = 0
        while True:
            self._ensure_allowed(scope, breaker, counted)
            if paced and self._pacer is not None:
                self._pacer.acquire()
            try:
                response = fn()
            except Exception as exc:
                delay = self._delay_for_failure(exc, attempt, breaker)
                logger.warning(
                    "Gemini %s call failed (%s); retry %d/%d in %.1fs.",
                    self.slot,
                    type(exc).__name__,
                    attempt + 1,
                    self._max_retries,
                    delay,
                )
                self._sleep(delay)
                attempt += 1
                continue
            if counted:
                self._record_response_cost(scope, response)
            return response

    def _ensure_allowed(
        self, scope: GeminiJobScope, breaker: CircuitBreaker, counted: bool
    ) -> None:
        scope.budget_breaker.raise_if_open()
        breaker.raise_if_open()
        if not counted:
            return
        exceeded = scope.budget.try_reserve_call()
        if exceeded is not None:
            scope.budget_breaker.trip(BreakerReason.BUDGET_EXCEEDED, exceeded)
            scope.budget_breaker.raise_if_open()

    def _delay_for_failure(
        self,
        exc: Exception,
        attempt: int,
        breaker: CircuitBreaker,
    ) -> float:
        classification = classify_gemini_error(exc)
        kind = classification.kind
        if kind is GeminiErrorKind.UNKNOWN:
            raise exc
        if kind in (GeminiErrorKind.PERMANENT, GeminiErrorKind.QUOTA_EXHAUSTED):
            reason = (
                BreakerReason.QUOTA_EXHAUSTED
                if kind is GeminiErrorKind.QUOTA_EXHAUSTED
                else BreakerReason.API_ERROR
            )
            detail = f"HTTP {classification.status_code}: {exc}"
            breaker.trip(reason, detail)
            raise GeminiCallBlockedError(self.slot, reason, detail[:_MAX_DETAIL_CHARS]) from exc
        if attempt >= self._max_retries:
            raise GeminiRetriesExhaustedError(
                f"Gemini {self.slot} call failed after {self._max_retries} retries: {exc}"
            ) from exc
        return self.backoff_delay(attempt, classification.retry_after_sec)

    def _record_response_cost(self, scope: GeminiJobScope, response: Any) -> None:
        usage = extract_usage_metadata(response)
        if usage is None:
            return
        scope.budget.add_cost(estimate_call_cost_usd(self.model, usage, self._pricing_table))
        max_cost = scope.budget.max_cost_usd
        if max_cost is not None and scope.budget.estimated_cost_usd >= max_cost:
            scope.budget_breaker.trip(
                BreakerReason.BUDGET_EXCEEDED,
                f"estimated cost ${scope.budget.estimated_cost_usd:.4f} reached "
                f"the ${max_cost:.2f} limit",
            )
