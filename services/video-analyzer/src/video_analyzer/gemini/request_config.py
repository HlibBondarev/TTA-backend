"""Builds ``GenerateContentConfig`` objects shared by every Gemini model slot."""

from __future__ import annotations

from typing import Any


def build_generate_content_config(
    types_module: Any,
    *,
    thinking_level: str | None = None,
    **config_kwargs: Any,
) -> Any:
    """Create a ``GenerateContentConfig`` with automatic function calling disabled.

    ``thinking_config`` is attached only when ``thinking_level`` is set. An unset level omits the
    field entirely, because some models (for example ``gemini-3.5-flash-lite``) reject it with
    HTTP 400. Only ``thinking_level`` is ever sent: never ``thinking_budget`` (the two are
    mutually exclusive) and never ``include_thoughts``.
    """
    config_kwargs["automatic_function_calling"] = types_module.AutomaticFunctionCallingConfig(
        disable=True
    )
    if thinking_level:
        config_kwargs["thinking_config"] = types_module.ThinkingConfig(
            thinking_level=types_module.ThinkingLevel(thinking_level.strip().upper())
        )
    return types_module.GenerateContentConfig(**config_kwargs)
