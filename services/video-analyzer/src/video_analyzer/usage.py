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
        source.get(field_name)
        if isinstance(source, Mapping)
        else getattr(source, field_name, None)
    )
    if raw is None or isinstance(raw, bool):
        return None
    if isinstance(raw, int):
        return max(0, raw)
    return None


def extract_usage_metadata(response: Any) -> GeminiTokenUsage | None:
    """Extract normalized token counters from a google-genai response's usage_metadata."""
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

    prompt_count = prompt or 0
    candidates_count = candidates or 0
    thoughts_count = thoughts or 0
    cached_count = cached or 0
    total_count = total if total is not None else prompt_count + candidates_count + thoughts_count

    return GeminiTokenUsage(
        call_count=1,
        prompt_tokens=prompt_count,
        candidates_tokens=candidates_count,
        thoughts_tokens=thoughts_count,
        cached_content_tokens=cached_count,
        total_tokens=total_count,
    )


def aggregate_token_usage(usages: Iterable[GeminiTokenUsage | None]) -> GeminiTokenUsage:
    """Sum token counters across multiple Gemini API calls, ignoring None entries."""
    call_count = 0
    prompt_tokens = 0
    candidates_tokens = 0
    thoughts_tokens = 0
    cached_content_tokens = 0
    total_tokens = 0

    for item in usages:
        if item is None:
            continue
        call_count += item.call_count
        prompt_tokens += item.prompt_tokens
        candidates_tokens += item.candidates_tokens
        thoughts_tokens += item.thoughts_tokens
        cached_content_tokens += item.cached_content_tokens
        total_tokens += item.total_tokens

    return GeminiTokenUsage(
        call_count=call_count,
        prompt_tokens=prompt_tokens,
        candidates_tokens=candidates_tokens,
        thoughts_tokens=thoughts_tokens,
        cached_content_tokens=cached_content_tokens,
        total_tokens=total_tokens,
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


def estimate_stage_cost(
    model_name: str,
    usage: GeminiTokenUsage,
    pricing_table: Mapping[str, Mapping[str, float]],
) -> GeminiCostEstimate:
    """Calculate estimated USD cost for a single stage's aggregated Gemini token usage.

    Thinking tokens (``thoughts_tokens``) are billed at the output token rate.
    ``total_tokens`` is intentionally not used for billing to avoid double-counting.
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

    input_rate, output_rate = rates
    input_cost = (usage.prompt_tokens / _TOKENS_PER_MILLION) * input_rate
    billable_output_tokens = usage.candidates_tokens + usage.thoughts_tokens
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


def _combine_stage_costs(
    stages: Iterable[GeminiStageUsageDiagnostics],
) -> GeminiCostEstimate | None:
    """Combine per-stage cost estimates across stages that recorded Gemini usage."""
    active_stages = [stage for stage in stages if stage.usage.call_count > 0]
    if not active_stages:
        return None

    unconfigured_models: list[str] = []
    input_total = 0.0
    output_total = 0.0
    combined_total = 0.0

    for stage in active_stages:
        estimate = stage.estimated_cost
        if estimate is None or not estimate.pricing_configured or estimate.total_cost_usd is None:
            model_label = normalize_gemini_model_name(stage.model or "") or (
                stage.model or "unknown"
            )
            if model_label not in unconfigured_models:
                unconfigured_models.append(model_label)
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
) -> GeminiUsageDiagnostics:
    """Aggregate Pass 1 and Pass 2 Gemini token usage and compute estimated USD costs."""
    p1_usage = aggregate_token_usage(pass1_usages)
    p2_usage = aggregate_token_usage(pass2_usages)
    total_usage = aggregate_token_usage((p1_usage, p2_usage))

    p1_cost = (
        estimate_stage_cost(pass1_model, p1_usage, pricing_table)
        if p1_usage.call_count > 0
        else None
    )
    p2_cost = (
        estimate_stage_cost(pass2_model, p2_usage, pricing_table)
        if p2_usage.call_count > 0
        else None
    )

    pass1_stage = GeminiStageUsageDiagnostics(
        model=pass1_model,
        usage=p1_usage,
        estimated_cost=p1_cost,
    )
    pass2_stage = GeminiStageUsageDiagnostics(
        model=pass2_model,
        usage=p2_usage,
        estimated_cost=p2_cost,
    )
    total_cost = _combine_stage_costs((pass1_stage, pass2_stage))

    return GeminiUsageDiagnostics(
        pass1=pass1_stage,
        pass2=pass2_stage,
        total=total_usage,
        estimated_cost=total_cost,
    )
