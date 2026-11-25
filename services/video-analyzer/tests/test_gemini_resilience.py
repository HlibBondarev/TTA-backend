"""Offline tests for the shared Gemini resilience layer (pacing, retries, breakers, budget)."""

from __future__ import annotations

import threading
from types import SimpleNamespace

import httpx
import pytest
from google.genai import errors as genai_errors

from video_analyzer.config import Settings
from video_analyzer.gemini.resilience import (
    BreakerReason,
    GeminiCallBlockedError,
    GeminiErrorKind,
    GeminiJobScope,
    GeminiResilience,
    GeminiRetriesExhaustedError,
    RpmPacer,
    classify_gemini_error,
    current_job_scope,
    extract_retry_delay_sec,
    job_scope,
)


def _api_error(code: int, status: str, *, delay: str | None = None, quota_id: str = "", msg=""):
    details: list[dict] = []
    if quota_id:
        details.append({"violations": [{"quotaId": quota_id}]})
    if delay is not None:
        details.append({"@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": delay})
    body = {"error": {"code": code, "message": msg or status, "status": status, "details": details}}
    error_cls = genai_errors.ClientError if code < 500 else genai_errors.ServerError
    return error_cls(code, body)


def _usage_response(prompt: int = 0, candidates: int = 0) -> SimpleNamespace:
    return SimpleNamespace(
        usage_metadata=SimpleNamespace(
            prompt_token_count=prompt,
            candidates_token_count=candidates,
            thoughts_token_count=0,
            cached_content_token_count=None,
            total_token_count=prompt + candidates,
        )
    )


class _Recorder:
    """Records sleep durations instead of sleeping."""

    def __init__(self) -> None:
        self.sleeps: list[float] = []

    def __call__(self, seconds: float) -> None:
        self.sleeps.append(seconds)


def _resilience(recorder: _Recorder, **kwargs) -> GeminiResilience:
    kwargs.setdefault("max_retries", 3)
    kwargs.setdefault("retry_base_sec", 2.0)
    return GeminiResilience("pass1", "gemini-4-hydrogen", sleep=recorder, rng=lambda: 0.0, **kwargs)


# ------------------------------------------------------------------ classification


@pytest.mark.parametrize("status_code", [400, 401, 403, 404])
def test_non_retryable_http_errors_are_permanent(status_code: int) -> None:
    error = _api_error(status_code, "INVALID_ARGUMENT")
    assert classify_gemini_error(error).kind is GeminiErrorKind.PERMANENT


@pytest.mark.parametrize("status_code", [500, 502, 503, 504])
def test_server_errors_are_transient(status_code: int) -> None:
    assert classify_gemini_error(_api_error(status_code, "UNAVAILABLE")).kind is (
        GeminiErrorKind.TRANSIENT
    )


def test_timeouts_and_transport_errors_are_transient_but_plain_errors_are_unknown() -> None:
    for exc in (TimeoutError("t"), ConnectionError("c"), httpx.ReadTimeout("slow")):
        assert classify_gemini_error(exc).kind is GeminiErrorKind.TRANSIENT
    assert classify_gemini_error(ValueError("bad arg")).kind is GeminiErrorKind.UNKNOWN


def test_short_per_minute_429_is_rate_limited_and_honors_server_delay() -> None:
    error = _api_error(
        429,
        "RESOURCE_EXHAUSTED",
        delay="37s",
        quota_id="GenerateRequestsPerMinutePerProjectPerModel-FreeTier",
    )
    classification = classify_gemini_error(error)
    assert classification.kind is GeminiErrorKind.RATE_LIMITED
    assert classification.retry_after_sec == 37.0
    assert classification.status_code == 429


def test_daily_or_long_delay_429_is_quota_exhausted() -> None:
    daily = _api_error(
        429, "RESOURCE_EXHAUSTED", quota_id="GenerateRequestsPerDayPerProjectPerModel-FreeTier"
    )
    assert classify_gemini_error(daily).kind is GeminiErrorKind.QUOTA_EXHAUSTED

    long_delay = _api_error(429, "RESOURCE_EXHAUSTED", delay="3600s")
    assert classify_gemini_error(long_delay).kind is GeminiErrorKind.QUOTA_EXHAUSTED

    boundary = _api_error(429, "RESOURCE_EXHAUSTED", delay="60s")
    assert classify_gemini_error(boundary).kind is GeminiErrorKind.RATE_LIMITED


def test_retry_delay_is_parsed_from_structured_details_and_message_text() -> None:
    assert extract_retry_delay_sec(_api_error(429, "RESOURCE_EXHAUSTED", delay="1.5s")) == 1.5
    assert extract_retry_delay_sec(
        _api_error(429, "RESOURCE_EXHAUSTED", msg="Quota exceeded. Please retry in 12.5s.")
    ) == pytest.approx(12.5)
    assert extract_retry_delay_sec(_api_error(429, "RESOURCE_EXHAUSTED")) is None


# ------------------------------------------------------------------------ pacer


def test_rpm_pacer_spaces_calls_evenly_and_never_waits_when_idle() -> None:
    now = [100.0]
    sleeps: list[float] = []

    def fake_sleep(seconds: float) -> None:
        sleeps.append(seconds)
        now[0] += seconds

    pacer = RpmPacer(rpm=60, clock=lambda: now[0], sleep=fake_sleep)
    assert pacer.acquire() == 0.0  # first call is immediate
    assert pacer.acquire() == pytest.approx(1.0)
    assert pacer.acquire() == pytest.approx(1.0)
    assert sleeps == [pytest.approx(1.0), pytest.approx(1.0)]

    now[0] += 30.0  # idle time does not bank extra burst capacity
    assert pacer.acquire() == 0.0
    assert pacer.acquire() == pytest.approx(1.0)

    with pytest.raises(ValueError):
        RpmPacer(rpm=0)


def test_rpm_pacer_reserves_distinct_slots_for_concurrent_threads() -> None:
    now = [0.0]
    pacer = RpmPacer(rpm=120, clock=lambda: now[0], sleep=lambda _: None)
    waits: list[float] = []
    lock = threading.Lock()

    def worker() -> None:
        wait = pacer.acquire()
        with lock:
            waits.append(wait)

    threads = [threading.Thread(target=worker) for _ in range(4)]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    assert sorted(waits) == [pytest.approx(w) for w in (0.0, 0.5, 1.0, 1.5)]


def test_pass_rpm_defaults_per_slot_come_from_settings() -> None:
    settings = Settings(_env_file=None)
    pass1 = GeminiResilience.from_settings(settings, "pass1")
    pass2 = GeminiResilience.from_settings(settings, "pass2")
    assert (settings.gemini_pass1_rpm, settings.gemini_pass2_rpm) == (60, 120)
    assert pass1._pacer._interval_sec == pytest.approx(1.0)
    assert pass2._pacer._interval_sec == pytest.approx(0.5)
    assert pass2.model == "gemini-3.5-flash-lite"
    with pytest.raises(ValueError, match="Unknown Gemini slot"):
        GeminiResilience.from_settings(settings, "nope")


def test_call_is_paced_before_every_attempt() -> None:
    now = [0.0]
    recorder = _Recorder()

    def fake_sleep(seconds: float) -> None:
        recorder(seconds)
        now[0] += seconds

    pacer = RpmPacer(rpm=30, clock=lambda: now[0], sleep=fake_sleep)
    resilience = GeminiResilience(
        "pass1", "m", pacer=pacer, max_retries=2, retry_base_sec=0.001, sleep=lambda _: None
    )
    attempts = iter([_api_error(503, "UNAVAILABLE"), None])

    def flaky():
        error = next(attempts)
        if error is not None:
            raise error
        return "ok"

    assert resilience.call(flaky) == "ok"
    assert recorder.sleeps == [pytest.approx(2.0)], "second attempt waited for its pacer slot"


# ----------------------------------------------------------------------- retries


def test_503_is_retried_with_exponential_jittered_backoff_then_succeeds() -> None:
    recorder = _Recorder()
    resilience = _resilience(recorder)  # rng=0 -> delay = exponential * 0.5
    attempts = {"n": 0}

    def flaky():
        attempts["n"] += 1
        if attempts["n"] < 4:
            raise _api_error(503, "UNAVAILABLE")
        return "ok"

    assert resilience.call(flaky) == "ok"
    assert attempts["n"] == 4
    assert recorder.sleeps == [pytest.approx(1.0), pytest.approx(2.0), pytest.approx(4.0)]


def test_backoff_is_jittered_between_half_and_full_exponential_delay() -> None:
    low = GeminiResilience("pass1", "m", retry_base_sec=2.0, rng=lambda: 0.0)
    high = GeminiResilience("pass1", "m", retry_base_sec=2.0, rng=lambda: 1.0)
    assert low.backoff_delay(2) == pytest.approx(4.0)
    assert high.backoff_delay(2) == pytest.approx(8.0)
    assert low.backoff_delay(20) == pytest.approx(30.0)  # capped at 60s * 0.5 jitter


def test_short_429_honors_server_retry_delay() -> None:
    recorder = _Recorder()
    resilience = _resilience(recorder)
    attempts = {"n": 0}

    def rate_limited():
        attempts["n"] += 1
        if attempts["n"] == 1:
            raise _api_error(
                429,
                "RESOURCE_EXHAUSTED",
                delay="12s",
                quota_id="GenerateRequestsPerMinutePerProjectPerModel",
            )
        return "ok"

    assert resilience.call(rate_limited) == "ok"
    assert recorder.sleeps[0] >= 12.0
    assert recorder.sleeps[0] <= 12.0 + 1.0 + 1e-9


def test_retries_exhausted_raises_without_opening_the_breaker() -> None:
    recorder = _Recorder()
    resilience = _resilience(recorder, max_retries=2)
    scope = GeminiJobScope()
    calls = {"n": 0}

    def always_503():
        calls["n"] += 1
        raise _api_error(503, "UNAVAILABLE")

    with job_scope(scope), pytest.raises(GeminiRetriesExhaustedError):
        resilience.call(always_503)
    assert calls["n"] == 3
    assert len(recorder.sleeps) == 2
    assert scope.open_breakers() == []


@pytest.mark.parametrize(
    ("error", "expected_reason"),
    [
        (_api_error(400, "INVALID_ARGUMENT"), BreakerReason.API_ERROR),
        (_api_error(401, "UNAUTHENTICATED"), BreakerReason.API_ERROR),
        (_api_error(403, "PERMISSION_DENIED"), BreakerReason.API_ERROR),
        (
            _api_error(429, "RESOURCE_EXHAUSTED", quota_id="GenerateRequestsPerDayPerModel"),
            BreakerReason.QUOTA_EXHAUSTED,
        ),
        (_api_error(429, "RESOURCE_EXHAUSTED", delay="900s"), BreakerReason.QUOTA_EXHAUSTED),
    ],
)
def test_non_retryable_errors_get_zero_retries_and_open_the_breaker(
    error: Exception, expected_reason: BreakerReason
) -> None:
    recorder = _Recorder()
    resilience = _resilience(recorder)
    scope = GeminiJobScope()
    calls = {"n": 0}

    def failing():
        calls["n"] += 1
        raise error

    with job_scope(scope):
        with pytest.raises(GeminiCallBlockedError) as exc_info:
            resilience.call(failing)
        assert exc_info.value.reason is expected_reason
        assert exc_info.value.__cause__ is error
        assert calls["n"] == 1 and recorder.sleeps == []

        # Subsequent calls are refused immediately without touching the API.
        with pytest.raises(GeminiCallBlockedError) as blocked:
            resilience.call(failing)
        assert blocked.value.reason is expected_reason
        assert calls["n"] == 1
        assert [b.name for b in scope.open_breakers()] == ["pass1"]


def test_unknown_exceptions_propagate_unchanged_without_retry_or_breaker() -> None:
    recorder = _Recorder()
    resilience = _resilience(recorder)
    scope = GeminiJobScope()

    def broken():
        raise ValueError("malformed request object")

    with job_scope(scope), pytest.raises(ValueError, match="malformed"):
        resilience.call(broken)
    assert recorder.sleeps == [] and scope.open_breakers() == []


def test_breaker_is_per_slot_but_budget_is_shared() -> None:
    scope = GeminiJobScope(max_calls=3)
    recorder = _Recorder()
    pass1 = _resilience(recorder)
    pass2 = GeminiResilience("pass2", "m", sleep=recorder)

    with job_scope(scope):
        with pytest.raises(GeminiCallBlockedError):
            pass1.call(lambda: (_ for _ in ()).throw(_api_error(403, "PERMISSION_DENIED")))
        # The Pass 2 slot is not affected by the Pass 1 breaker, but spends the shared budget.
        assert pass2.call(lambda: "ok") == "ok"
        assert pass2.call(lambda: "ok") == "ok"
        assert scope.budget.calls == 3
        with pytest.raises(GeminiCallBlockedError) as exc_info:
            pass2.call(lambda: "never")
        assert exc_info.value.reason is BreakerReason.BUDGET_EXCEEDED


# ------------------------------------------------------------------------ budget


def test_max_calls_per_job_is_enforced_and_counts_every_attempt() -> None:
    recorder = _Recorder()
    resilience = _resilience(recorder)
    scope = GeminiJobScope(max_calls=3)
    calls = {"n": 0}

    def flaky():
        calls["n"] += 1
        if calls["n"] == 1:
            raise _api_error(503, "UNAVAILABLE")
        return "ok"

    with job_scope(scope):
        assert resilience.call(flaky) == "ok"  # 2 attempts
        assert resilience.call(lambda: "ok") == "ok"  # 3rd
        with pytest.raises(GeminiCallBlockedError) as exc_info:
            resilience.call(lambda: "blocked")
    assert exc_info.value.reason is BreakerReason.BUDGET_EXCEEDED
    assert "GEMINI_MAX_CALLS_PER_JOB" in exc_info.value.detail
    assert scope.budget.calls == 3
    assert [b.name for b in scope.open_breakers()] == ["budget"]


def test_max_estimated_cost_is_enforced_from_response_usage() -> None:
    resilience = _resilience(_Recorder())  # gemini-4-hydrogen: $0.50 in / $3.00 out per 1M
    scope = GeminiJobScope(max_cost_usd=1.0)
    expensive = _usage_response(prompt=1_000_000, candidates=100_000)  # $0.50 + $0.30 = $0.80

    with job_scope(scope):
        resilience.call(lambda: expensive)
        assert scope.budget.estimated_cost_usd == pytest.approx(0.8)
        resilience.call(lambda: expensive)  # crosses the $1.00 ceiling after this response
        assert scope.budget.estimated_cost_usd == pytest.approx(1.6)
        with pytest.raises(GeminiCallBlockedError) as exc_info:
            resilience.call(lambda: expensive)
    assert exc_info.value.reason is BreakerReason.BUDGET_EXCEEDED
    assert "estimated cost" in exc_info.value.detail
    assert scope.budget.calls == 2


def test_unpriced_models_and_missing_usage_do_not_trip_the_cost_breaker() -> None:
    scope = GeminiJobScope(max_cost_usd=0.0001)
    unpriced = GeminiResilience("pass2", "totally-unknown-model")
    with job_scope(scope):
        unpriced.call(lambda: _usage_response(prompt=9_000_000, candidates=9_000_000))
        unpriced.call(lambda: SimpleNamespace(usage_metadata=None))
        unpriced.call(lambda: "plain")
    assert scope.budget.estimated_cost_usd == 0.0
    assert scope.open_breakers() == []


def test_uncounted_calls_skip_the_budget_and_are_not_paced() -> None:
    scope = GeminiJobScope(max_calls=1)
    now = [0.0]
    pacer = RpmPacer(rpm=1, clock=lambda: now[0], sleep=lambda s: pytest.fail("must not pace"))
    resilience = GeminiResilience("pass1", "m", pacer=pacer)
    with job_scope(scope):
        for _ in range(3):
            assert resilience.call(lambda: "upload", paced=False, counted=False) == "upload"
    assert scope.budget.calls == 0


def test_job_scope_isolates_concurrent_jobs_and_restores_previous_scope() -> None:
    assert current_job_scope() is None
    outer, inner = GeminiJobScope(max_calls=1), GeminiJobScope(max_calls=1)
    resilience = _resilience(_Recorder())
    with job_scope(outer):
        resilience.call(lambda: "ok")
        with job_scope(inner):
            assert current_job_scope() is inner
            resilience.call(lambda: "ok")  # inner scope has its own budget
        assert current_job_scope() is outer
        with pytest.raises(GeminiCallBlockedError):
            resilience.call(lambda: "ok")
    assert current_job_scope() is None
