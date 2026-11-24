"""Unit tests for Gemini default models, usage_metadata aggregation, and USD cost estimation."""

from __future__ import annotations

import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from video_analyzer.config import (
    DEFAULT_GEMINI_MODEL,
    DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    Settings,
    normalize_gemini_model_name,
)
from video_analyzer.pass1.gemini_client import GeminiPass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult
from video_analyzer.pass2.cap_resolver import GeminiCropGridCapResolver
from video_analyzer.schemas.draft import GeminiTokenUsage
from video_analyzer.usage import (
    aggregate_token_usage,
    build_gemini_usage_diagnostics,
    estimate_stage_cost,
    extract_usage_metadata,
)


def test_default_gemini_models_and_independent_overrides(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("GEMINI_PASS1_MODEL", raising=False)
    monkeypatch.delenv("GEMINI_PASS2_MODEL", raising=False)
    monkeypatch.delenv("GEMINI_MODEL_PRICING_USD_PER_1M", raising=False)

    defaults = Settings(_env_file=None)
    assert defaults.gemini_pass1_model == "gemini-4-hydrogen"
    assert defaults.gemini_pass2_model == "gemini-4-hydrogen"
    assert DEFAULT_GEMINI_MODEL == "gemini-4-hydrogen"
    assert GeminiPass1Analyzer(api_key="test-key")._model_name == "gemini-4-hydrogen"
    assert GeminiCropGridCapResolver(api_key="test-key")._model_name == "gemini-4-hydrogen"

    monkeypatch.setenv("GEMINI_PASS1_MODEL", "gemini-3.8-flash")
    monkeypatch.setenv("GEMINI_PASS2_MODEL", "gemini-3.5-flash-lite")
    overridden = Settings(_env_file=None)
    assert overridden.gemini_pass1_model == "gemini-3.8-flash"
    assert overridden.gemini_pass2_model == "gemini-3.5-flash-lite"


def test_pass1_chunk_result_json_schema_excludes_token_usage() -> None:
    schema = Pass1ChunkResult.model_json_schema()
    assert "token_usage" not in schema.get("properties", {})
    assert set(schema.get("properties", {}).keys()) == {"period_events", "time_anchors"}

    chunk = Pass1ChunkResult(
        period_events=[],
        time_anchors=[],
        token_usage=GeminiTokenUsage(call_count=1, prompt_tokens=10, total_tokens=10),
    )
    assert "token_usage" not in chunk.model_dump()


def test_normalize_gemini_model_name() -> None:
    assert normalize_gemini_model_name("  models/Gemini-4-Hydrogen  ") == "gemini-4-hydrogen"
    assert normalize_gemini_model_name("MODELS/gemini-3.8-flash") == "gemini-3.8-flash"
    assert normalize_gemini_model_name("gemini-3.5-flash-lite") == "gemini-3.5-flash-lite"


def test_extract_usage_metadata_handles_full_partial_and_missing_values() -> None:
    full_response = SimpleNamespace(
        usage_metadata=SimpleNamespace(
            prompt_token_count=1200,
            candidates_token_count=300,
            thoughts_token_count=150,
            cached_content_token_count=200,
            total_token_count=1650,
        )
    )
    full = extract_usage_metadata(full_response)
    assert full is not None
    assert full.call_count == 1
    assert full.prompt_tokens == 1200
    assert full.candidates_tokens == 300
    assert full.thoughts_tokens == 150
    assert full.cached_content_tokens == 200
    assert full.total_tokens == 1650

    partial_response = SimpleNamespace(
        usage_metadata=SimpleNamespace(
            prompt_token_count=500,
            candidates_token_count=100,
            thoughts_token_count=None,
            cached_content_token_count=None,
            total_token_count=None,
        )
    )
    partial = extract_usage_metadata(partial_response)
    assert partial is not None
    assert partial.call_count == 1
    assert partial.prompt_tokens == 500
    assert partial.candidates_tokens == 100
    assert partial.thoughts_tokens is None
    assert partial.cached_content_tokens is None
    assert partial.total_tokens is None

    prompt_only = extract_usage_metadata(
        SimpleNamespace(
            usage_metadata=SimpleNamespace(
                prompt_token_count=400,
                candidates_token_count=None,
                thoughts_token_count=None,
                cached_content_token_count=None,
                total_token_count=None,
            )
        )
    )
    assert prompt_only is not None
    assert prompt_only.prompt_tokens == 400
    assert prompt_only.candidates_tokens is None
    assert prompt_only.thoughts_tokens is None

    assert extract_usage_metadata(SimpleNamespace(text="ok")) is None
    assert extract_usage_metadata(SimpleNamespace(usage_metadata=None)) is None
    assert (
        extract_usage_metadata(
            SimpleNamespace(
                usage_metadata=SimpleNamespace(
                    prompt_token_count=None,
                    candidates_token_count=None,
                    thoughts_token_count=None,
                    cached_content_token_count=None,
                    total_token_count=None,
                )
            )
        )
        is None
    )


def test_aggregate_token_usage_sums_across_calls_and_ignores_none() -> None:
    u1 = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=1000,
        candidates_tokens=200,
        thoughts_tokens=50,
        cached_content_tokens=100,
        total_tokens=1250,
    )
    u2 = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=2000,
        candidates_tokens=400,
        thoughts_tokens=100,
        cached_content_tokens=0,
        total_tokens=2500,
    )
    agg = aggregate_token_usage([u1, None, u2])
    assert agg.call_count == 2
    assert agg.prompt_tokens == 3000
    assert agg.candidates_tokens == 600
    assert agg.thoughts_tokens == 150
    assert agg.cached_content_tokens == 100
    assert agg.total_tokens == 3750


def test_estimate_stage_cost_bills_thinking_tokens_at_output_rate_without_double_counting() -> None:
    usage = GeminiTokenUsage(
        call_count=2,
        prompt_tokens=1_000_000,
        candidates_tokens=250_000,
        thoughts_tokens=250_000,
        cached_content_tokens=0,
        # Intentionally inflated total_tokens to verify billing uses prompt + candidates + thoughts
        total_tokens=9_999_999,
    )
    cost = estimate_stage_cost(
        "  models/gemini-4-hydrogen ",
        usage,
        DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert cost.is_estimate is True
    assert cost.pricing_configured is True
    assert cost.currency == "USD"
    # 1M * $0.50 = $0.50 input; (250k + 250k) * $3.00 / 1M = $1.50 output
    assert cost.input_cost_usd == pytest.approx(0.50)
    assert cost.output_cost_usd == pytest.approx(1.50)
    assert cost.total_cost_usd == pytest.approx(2.00)


def test_custom_pricing_overrides_via_env_json_and_file(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    monkeypatch.setenv(
        "GEMINI_MODEL_PRICING_USD_PER_1M",
        json.dumps(
            {
                "models/gemini-4-hydrogen": {"input": 1.00, "output": 4.00},
                "custom-gemini-next": {"input": 0.20, "output": 0.80},
            }
        ),
    )
    settings = Settings(_env_file=None)
    pricing = settings.resolved_gemini_model_pricing
    assert pricing["gemini-4-hydrogen"] == {"input": 1.00, "output": 4.00}
    assert pricing["custom-gemini-next"] == {"input": 0.20, "output": 0.80}
    # Default models not overridden remain available
    assert "gemini-3.5-flash-lite" in pricing

    pricing_file = tmp_path / "pricing.json"
    pricing_file.write_text(
        json.dumps({"gemini-custom-file": {"input": 0.75, "output": 2.25}}),
        encoding="utf-8",
    )
    monkeypatch.setenv("GEMINI_MODEL_PRICING_USD_PER_1M", str(pricing_file))
    file_settings = Settings(_env_file=None)
    assert file_settings.resolved_gemini_model_pricing["gemini-custom-file"] == {
        "input": 0.75,
        "output": 2.25,
    }


def test_mixed_pass1_and_pass2_models_pricing_and_unconfigured_fallback() -> None:
    p1_usage = GeminiTokenUsage(
        call_count=2,
        prompt_tokens=1_000_000,
        candidates_tokens=100_000,
        thoughts_tokens=100_000,
        total_tokens=1_200_000,
    )
    p2_usage = GeminiTokenUsage(
        call_count=3,
        prompt_tokens=500_000,
        candidates_tokens=50_000,
        thoughts_tokens=50_000,
        total_tokens=600_000,
    )

    # 1. Both stages use different configured models
    diag_both = build_gemini_usage_diagnostics(
        pass1_model="models/gemini-4-hydrogen",
        pass1_usages=[p1_usage],
        pass2_model="gemini-3.5-flash-lite",
        pass2_usages=[p2_usage],
        pricing_table=DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert diag_both.pass1.estimated_cost is not None
    assert diag_both.pass2.estimated_cost is not None
    assert diag_both.estimated_cost is not None
    assert diag_both.estimated_cost.pricing_configured is True
    # Pass 1: 1.0*0.50 + 0.2*3.00 = 0.50 + 0.60 = 1.10
    # Pass 2: 0.5*0.10 + 0.1*0.40 = 0.05 + 0.04 = 0.09
    assert diag_both.pass1.estimated_cost.total_cost_usd == pytest.approx(1.10)
    assert diag_both.pass2.estimated_cost.total_cost_usd == pytest.approx(0.09)
    assert diag_both.estimated_cost.input_cost_usd == pytest.approx(0.55)
    assert diag_both.estimated_cost.output_cost_usd == pytest.approx(0.64)
    assert diag_both.estimated_cost.total_cost_usd == pytest.approx(1.19)

    # 2. Mixed: Pass 1 configured, Pass 2 unconfigured (both active) -> totalCostUsd must be None
    diag_mixed_unknown = build_gemini_usage_diagnostics(
        pass1_model="gemini-4-hydrogen",
        pass1_usages=[p1_usage],
        pass2_model="gemini-experimental-unknown",
        pass2_usages=[p2_usage],
        pricing_table=DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert diag_mixed_unknown.pass1.estimated_cost is not None
    assert diag_mixed_unknown.pass1.estimated_cost.pricing_configured is True
    assert diag_mixed_unknown.pass1.estimated_cost.total_cost_usd == pytest.approx(1.10)

    assert diag_mixed_unknown.pass2.estimated_cost is not None
    assert diag_mixed_unknown.pass2.estimated_cost.pricing_configured is False
    assert diag_mixed_unknown.pass2.estimated_cost.total_cost_usd is None

    assert diag_mixed_unknown.estimated_cost is not None
    assert diag_mixed_unknown.estimated_cost.pricing_configured is False
    assert diag_mixed_unknown.estimated_cost.input_cost_usd is None
    assert diag_mixed_unknown.estimated_cost.output_cost_usd is None
    assert diag_mixed_unknown.estimated_cost.total_cost_usd is None
    assert "gemini-experimental-unknown" in (diag_mixed_unknown.estimated_cost.note or "")

    # 3. Pass 1 active & configured, Pass 2 inactive (0 calls) -> total equals Pass 1
    diag_inactive_p2 = build_gemini_usage_diagnostics(
        pass1_model="gemini-4-hydrogen",
        pass1_usages=[p1_usage],
        pass2_model="gemini-experimental-unknown",
        pass2_usages=[],
        pricing_table=DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert diag_inactive_p2.pass2.estimated_cost is None
    assert diag_inactive_p2.estimated_cost is not None
    assert diag_inactive_p2.estimated_cost.pricing_configured is True
    assert diag_inactive_p2.estimated_cost.total_cost_usd == pytest.approx(1.10)


def test_pricing_validation_rejects_non_finite_and_negative_rates_but_allows_zero() -> None:
    zero_settings = Settings(
        _env_file=None,
        gemini_model_pricing_usd_per_1m={"gemini-4-hydrogen": {"input": 0.0, "output": 0}},
    )
    assert zero_settings.resolved_gemini_model_pricing["gemini-4-hydrogen"] == {
        "input": 0.0,
        "output": 0.0,
    }

    for bad_rate in (float("nan"), float("inf"), float("-inf"), -0.01):
        with pytest.raises(ValueError, match="finite non-negative"):
            Settings(
                _env_file=None,
                gemini_model_pricing_usd_per_1m={
                    "gemini-4-hydrogen": {"input": bad_rate, "output": 1.0}
                },
            )
        with pytest.raises(ValueError, match="finite non-negative"):
            Settings(
                _env_file=None,
                gemini_model_pricing_usd_per_1m={
                    "gemini-4-hydrogen": {"input": 1.0, "output": bad_rate}
                },
            )


def test_estimate_stage_cost_handles_nullable_counters_and_non_thinking_models() -> None:
    # 1. Missing prompt_tokens or candidates_tokens -> unavailable cost with note
    missing_candidates = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=1_000_000,
        candidates_tokens=None,
        thoughts_tokens=None,
        total_tokens=1_000_000,
    )
    cost_missing = estimate_stage_cost(
        "gemini-4-hydrogen",
        missing_candidates,
        DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert cost_missing.pricing_configured is True
    assert cost_missing.input_cost_usd is None
    assert cost_missing.output_cost_usd is None
    assert cost_missing.total_cost_usd is None
    assert cost_missing.note is not None

    # 2. thoughts_tokens is None AND total_tokens > prompt + candidates -> unaccounted tokens
    unaccounted = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=1_000_000,
        candidates_tokens=100_000,
        thoughts_tokens=None,
        total_tokens=1_250_000,
    )
    cost_unaccounted = estimate_stage_cost(
        "gemini-4-hydrogen",
        unaccounted,
        DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert cost_unaccounted.pricing_configured is True
    assert cost_unaccounted.total_cost_usd is None
    assert "thoughts_token_count" in (cost_unaccounted.note or "")

    # 3. Non-thinking model: thoughts_tokens is None, total_tokens absent -> billable cleanly
    non_thinking_no_total = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=1_000_000,
        candidates_tokens=100_000,
        thoughts_tokens=None,
        total_tokens=None,
    )
    cost_nt1 = estimate_stage_cost(
        "gemini-4-hydrogen",
        non_thinking_no_total,
        DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert non_thinking_no_total.thoughts_tokens is None
    assert cost_nt1.pricing_configured is True
    assert cost_nt1.input_cost_usd == pytest.approx(0.50)
    assert cost_nt1.output_cost_usd == pytest.approx(0.30)
    assert cost_nt1.total_cost_usd == pytest.approx(0.80)

    # 4. Non-thinking model: thoughts_tokens is None, total_tokens matches sum -> billable
    non_thinking_exact_total = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=1_000_000,
        candidates_tokens=100_000,
        thoughts_tokens=None,
        total_tokens=1_100_000,
    )
    cost_nt2 = estimate_stage_cost(
        "gemini-4-hydrogen",
        non_thinking_exact_total,
        DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert non_thinking_exact_total.thoughts_tokens is None
    assert cost_nt2.pricing_configured is True
    assert cost_nt2.total_cost_usd == pytest.approx(0.80)


def test_aggregate_preserves_unavailable_cost_across_mixed_partial_calls() -> None:
    call_with_unaccounted = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=1_000_000,
        candidates_tokens=100_000,
        thoughts_tokens=None,
        total_tokens=1_250_000,
    )
    call_without_total = GeminiTokenUsage(
        call_count=1,
        prompt_tokens=500_000,
        candidates_tokens=50_000,
        thoughts_tokens=20_000,
        total_tokens=None,
    )

    aggregated = aggregate_token_usage([call_with_unaccounted, call_without_total])
    assert aggregated.call_count == 2
    assert aggregated.prompt_tokens == 1_500_000
    assert aggregated.candidates_tokens == 150_000
    assert aggregated.thoughts_tokens is None
    assert aggregated.total_tokens is None
    assert "hasUnaccountedTokens" not in aggregated.model_dump(by_alias=True)

    stage_cost = estimate_stage_cost(
        "gemini-4-hydrogen",
        aggregated,
        DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert stage_cost.pricing_configured is True
    assert stage_cost.input_cost_usd is None
    assert stage_cost.output_cost_usd is None
    assert stage_cost.total_cost_usd is None
    assert "thoughts_token_count" in (stage_cost.note or "")

    diag = build_gemini_usage_diagnostics(
        pass1_model="gemini-4-hydrogen",
        pass1_usages=[call_with_unaccounted, call_without_total],
        pass2_model="gemini-4-hydrogen",
        pass2_usages=[],
        pricing_table=DEFAULT_GEMINI_MODEL_PRICING_USD_PER_1M,
    )
    assert diag.pass1.estimated_cost is not None
    assert diag.pass1.estimated_cost.total_cost_usd is None
    assert diag.estimated_cost is not None
    assert diag.estimated_cost.pricing_configured is True
    assert diag.estimated_cost.total_cost_usd is None
