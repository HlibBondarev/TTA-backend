"""Gemini API token usage extraction, aggregation, and estimated USD cost calculation."""

from __future__ import annotations

from collections.abc import Iterable, Mapping
from typing import Any

from video_analyzer.config import normalize_gemini_model_name
from video_analyzer.schemas.draft import (
    GeminiCostEstimate,
    GeminiStageUsageDiagnostics,
    GeminiTokenUsage,
    GeminiUsageDiagnostics,
)

_TOKENS_PER_MILLION: float = 1_000_000.0
_USD_DECIMAL_PLACES: int = 6


def _read_non_negative_int(source: Any, field_name: str) -> int | None:
    """Safely extract a non-negative integer counter from an object or mapping."""
    raw = (
        source.get(field_name) if isinstance(source, Mapping) else getattr(source, field_name, None)
    )
    if raw is None or isinstance(raw, bool):
        return None
    if isinstance(raw, int):
        return max(0, raw)
    return None


def extract_usage_metadata(response: Any) -> GeminiTokenUsage | None:
    """Extract normalized token counters from a google-genai response's usage_metadata.

    Missing counters are preserved as ``None`` rather than defaulted to ``0``.
    """
    metadata = (
        response.get("usage_metadata")
        if isinstance(response, Mapping)
        else getattr(response, "usage_metadata", None)
    )
    if metadata is None:
        return None

    prompt = _read_non_negative_int(metadata, "prompt_token_count")
    candidates = _read_non_negative_int(metadata, "candidates_token_count")
    thoughts = _read_non_negative_int(metadata, "thoughts_token_count")
    cached = _read_non_negative_int(metadata, "cached_content_token_count")
    total = _read_non_negative_int(metadata, "total_token_count")

    if all(value is None for value in (prompt, candidates, thoughts, cached, total)):
        return None

    return GeminiTokenUsage(
        call_count=1,
        prompt_tokens=prompt,
        candidates_tokens=candidates,
        thoughts_tokens=thoughts,
        cached_content_tokens=cached,
        total_tokens=total,
    )


def _has_unaccounted_non_thought_tokens(item: GeminiTokenUsage) -> bool:
    """Return True when unaccounted tokens exist while thoughts_tokens is absent."""
    if item.has_unaccounted_tokens:
        return True
    if item.thoughts_tokens is not None:
        return False
    if item.prompt_tokens is None or item.candidates_tokens is None or item.total_tokens is None:
        return False
    return item.total_tokens > (item.prompt_tokens + item.candidates_tokens)


def _sum_strict_counter(items: list[GeminiTokenUsage], attr: str) -> int | None:
    """Sum a counter across active calls; return None if any active call omitted it."""
    values = [getattr(item, attr) for item in items]
    if any(value is None for value in values):
        return None
    return sum(values)


def _sum_optional_counter(items: list[GeminiTokenUsage], attr: str) -> int | None:
    """Sum an optional counter across active calls; return None if every call omitted it."""
    values = [getattr(item, attr) for item in items if getattr(item, attr) is not None]
    return sum(values) if values else None


def aggregate_token_usage(usages: Iterable[GeminiTokenUsage | None]) -> GeminiTokenUsage:
    """Sum token counters across multiple Gemini API calls, ignoring None and zero-call entries."""
    active = [item for item in usages if item is not None and item.call_count > 0]
    if not active:
        return GeminiTokenUsage()

    has_unaccounted = any(_has_unaccounted_non_thought_tokens(item) for item in active)
    thoughts_tokens = None if has_unaccounted else _sum_optional_counter(active, "thoughts_tokens")
    return GeminiTokenUsage(
        call_count=sum(item.call_count for item in active),
        prompt_tokens=_sum_strict_counter(active, "prompt_tokens"),
        candidates_tokens=_sum_strict_counter(active, "candidates_tokens"),
        thoughts_tokens=thoughts_tokens,
        cached_content_tokens=_sum_optional_counter(active, "cached_content_tokens"),
        total_tokens=_sum_strict_counter(active, "total_tokens"),
        has_unaccounted_tokens=has_unaccounted,
    )


def _lookup_model_pricing(
    model_name: str,
    pricing_table: Mapping[str, Mapping[str, float]],
) -> tuple[float, float] | None:
    """Resolve (input_usd_per_1m, output_usd_per_1m) for a model name after normalization."""
    normalized_target = normalize_gemini_model_name(model_name)
    if not normalized_target:
        return None

    direct = pricing_table.get(normalized_target)
    if direct is not None:
        return float(direct["input"]), float(direct["output"])

    for raw_key, rates in pricing_table.items():
        if normalize_gemini_model_name(str(raw_key)) == normalized_target:
            return float(rates["input"]), float(rates["output"])
    return None


def _resolve_billable_tokens(usage: GeminiTokenUsage) -> tuple[int, int] | str:
    """Return (prompt_tokens, billable_output_tokens) or an explanatory error note."""
    if usage.prompt_tokens is None or usage.candidates_tokens is None:
        return (
            "Estimated cost unavailable due to incomplete token usage metadata "
            "(missing prompt or candidates token count)."
        )

    if usage.thoughts_tokens is None:
        if _has_unaccounted_non_thought_tokens(usage):
            return (
                "Estimated cost unavailable because total_token_count exceeds "
                "prompt + candidates tokens while thoughts_token_count is missing."
            )
        return usage.prompt_tokens, usage.candidates_tokens

    return usage.prompt_tokens, usage.candidates_tokens + usage.thoughts_tokens


def estimate_stage_cost(
    model_name: str,
    usage: GeminiTokenUsage,
    pricing_table: Mapping[str, Mapping[str, float]],
) -> GeminiCostEstimate:
    """Calculate estimated USD cost for a single stage's aggregated Gemini token usage.

    Thinking tokens (``thoughts_tokens``) are billed at the output token rate.
    When ``thoughts_tokens`` is absent and ``total_tokens`` is either absent or matches
    ``prompt_tokens + candidates_tokens``, absent thinking tokens are treated as 0 for billing.
    """
    rates = _lookup_model_pricing(model_name, pricing_table)
    if rates is None:
        normalized = normalize_gemini_model_name(model_name) or model_name
        return GeminiCostEstimate(
            currency="USD",
            is_estimate=True,
            pricing_configured=False,
            input_cost_usd=None,
            output_cost_usd=None,
            total_cost_usd=None,
            note=f"No pricing configured for model '{normalized}'.",
        )

    billable = _resolve_billable_tokens(usage)
    if isinstance(billable, str):
        return GeminiCostEstimate(
            currency="USD",
            is_estimate=True,
            pricing_configured=True,
            input_cost_usd=None,
            output_cost_usd=None,
            total_cost_usd=None,
            note=billable,
        )

    prompt_tokens, billable_output_tokens = billable
    input_rate, output_rate = rates
    input_cost = (prompt_tokens / _TOKENS_PER_MILLION) * input_rate
    output_cost = (billable_output_tokens / _TOKENS_PER_MILLION) * output_rate
    total_cost = input_cost + output_cost

    return GeminiCostEstimate(
        currency="USD",
        is_estimate=True,
        pricing_configured=True,
        input_cost_usd=round(input_cost, _USD_DECIMAL_PLACES),
        output_cost_usd=round(output_cost, _USD_DECIMAL_PLACES),
        total_cost_usd=round(total_cost, _USD_DECIMAL_PLACES),
    )


def estimate_call_cost_usd(
    model_name: str,
    usage: GeminiTokenUsage,
    pricing_table: Mapping[str, Mapping[str, float]],
) -> float:
    """Estimate the USD cost of one call for budget enforcement.

    The figure is intentionally a conservative upper bound: every prompt token is billed at the
    full input rate and cached-content discounts are ignored, so the budget breaker never
    under-counts spend and its behavior stays predictable.

    Returns ``0.0`` when pricing or token metadata is unavailable, so an unpriced model never
    trips the cost breaker (the call-count limit still applies).
    """
    estimate = estimate_stage_cost(model_name, usage, pricing_table)
    return estimate.total_cost_usd or 0.0


def _combine_stage_costs(
    stages: Iterable[GeminiStageUsageDiagnostics],
) -> GeminiCostEstimate | None:
    """Combine per-stage cost estimates across stages that recorded Gemini usage."""
    active_stages = [stage for stage in stages if stage.usage.call_count > 0]
    if not active_stages:
        return None

    unconfigured_models: list[str] = []
    has_incomplete_metadata = False
    input_total = 0.0
    output_total = 0.0
    combined_total = 0.0

    for stage in active_stages:
        estimate = stage.estimated_cost
        if estimate is None or not estimate.pricing_configured:
            model_label = normalize_gemini_model_name(stage.model or "") or (
                stage.model or "unknown"
            )
            if model_label not in unconfigured_models:
                unconfigured_models.append(model_label)
            continue
        if estimate.total_cost_usd is None:
            has_incomplete_metadata = True
            continue
        input_total += estimate.input_cost_usd or 0.0
        output_total += estimate.output_cost_usd or 0.0
        combined_total += estimate.total_cost_usd

    if unconfigured_models:
        models_str = ", ".join(unconfigured_models)
        return GeminiCostEstimate(
            currency="USD",
            is_estimate=True,
            pricing_configured=False,
            input_cost_usd=None,
            output_cost_usd=None,
            total_cost_usd=None,
            note=(
                "Total estimated cost unavailable; "
                f"unconfigured pricing for model(s): {models_str}."
            ),
        )

    if has_incomplete_metadata:
        return GeminiCostEstimate(
            currency="USD",
            is_estimate=True,
            pricing_configured=True,
            input_cost_usd=None,
            output_cost_usd=None,
            total_cost_usd=None,
            note=(
                "Total estimated cost unavailable due to incomplete token usage metadata "
                "in one or more active stages."
            ),
        )

    return GeminiCostEstimate(
        currency="USD",
        is_estimate=True,
        pricing_configured=True,
        input_cost_usd=round(input_total, _USD_DECIMAL_PLACES),
        output_cost_usd=round(output_total, _USD_DECIMAL_PLACES),
        total_cost_usd=round(combined_total, _USD_DECIMAL_PLACES),
    )


def build_gemini_usage_diagnostics(
    *,
    pass1_model: str,
    pass1_usages: Iterable[GeminiTokenUsage | None],
    pass2_model: str,
    pass2_usages: Iterable[GeminiTokenUsage | None],
    pricing_table: Mapping[str, Mapping[str, float]],
    verifier_model: str | None = None,
    verifier_usages: Iterable[GeminiTokenUsage | None] = (),
) -> GeminiUsageDiagnostics:
    """Aggregate per-slot Gemini token usage (Pass 1, verifier, Pass 2) and estimated USD cost."""
    pass1_stage = _build_stage_diagnostics(pass1_model, pass1_usages, pricing_table)
    pass2_stage = _build_stage_diagnostics(pass2_model, pass2_usages, pricing_table)
    verifier_stage = _build_stage_diagnostics(verifier_model, verifier_usages, pricing_table)
    stages = (pass1_stage, verifier_stage, pass2_stage)
    total_usage = aggregate_token_usage(stage.usage for stage in stages)

    return GeminiUsageDiagnostics(
        pass1=pass1_stage,
        verifier=verifier_stage,
        pass2=pass2_stage,
        total=total_usage,
        estimated_cost=_combine_stage_costs(stages),
    )


def _build_stage_diagnostics(
    model_name: str | None,
    usages: Iterable[GeminiTokenUsage | None],
    pricing_table: Mapping[str, Mapping[str, float]],
) -> GeminiStageUsageDiagnostics:
    usage = aggregate_token_usage(usages)
    cost = (
        estimate_stage_cost(model_name, usage, pricing_table)
        if model_name is not None and usage.call_count > 0
        else None
    )
    return GeminiStageUsageDiagnostics(model=model_name, usage=usage, estimated_cost=cost)
