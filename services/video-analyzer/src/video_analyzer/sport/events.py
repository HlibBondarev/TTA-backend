"""Internal event catalog with deterministic, Python-generated ``promptCode`` values."""

from __future__ import annotations

import re
from collections.abc import Sequence
from dataclasses import dataclass
from uuid import UUID

from video_analyzer.schemas.draft import FoulImpact
from video_analyzer.schemas.request import EventDefinitionSnapshot

_NON_ALNUM = re.compile(r"[^A-Z0-9]+")
_FALLBACK_SHORT_CODE = "X"


@dataclass(frozen=True, slots=True)
class EventDefinition:
    """A system event definition enriched with its internal prompt code.

    ``event_definition_id`` is the canonical identity (the .NET ``eventDefinitions[].id``);
    ``prompt_code`` is an internal identifier shown to the VLM and never part of the .NET
    contract.
    """

    prompt_code: str
    event_definition_id: UUID
    name: str
    short_name: str
    is_positive: bool
    is_shot: bool
    score: int
    foul_impact: FoulImpact
    penalty_duration_seconds: int | None


def _short_code(short_name: str) -> str:
    cleaned = _NON_ALNUM.sub("", short_name.upper())
    return cleaned or _FALLBACK_SHORT_CODE


def _sort_key(definition: EventDefinitionSnapshot) -> tuple[bool, str, str, str]:
    """Order-independent key: positive events first, then name, short name and id."""
    return (
        not definition.is_positive,
        definition.name.casefold(),
        definition.short_name.casefold(),
        str(definition.id),
    )


def _format_prompt_code(index: int, definition: EventDefinitionSnapshot) -> str:
    polarity = "POS" if definition.is_positive else "NEG"
    return f"EVT_{index:02d}_{_short_code(definition.short_name)}_{polarity}"


def build_event_catalog(
    definitions: Sequence[EventDefinitionSnapshot],
) -> list[EventDefinition]:
    """Generate unique, deterministic prompt codes such as ``EVT_01_G_POS``.

    The numeric index follows an input-order-independent sort, so the same set of definitions
    always yields the same codes. The returned list preserves the input order.
    """
    ranked = sorted(definitions, key=_sort_key)
    codes = {item.id: _format_prompt_code(index, item) for index, item in enumerate(ranked, 1)}
    return [
        EventDefinition(
            prompt_code=codes[item.id],
            event_definition_id=item.id,
            name=item.name,
            short_name=item.short_name,
            is_positive=item.is_positive,
            is_shot=item.is_shot,
            score=item.score,
            foul_impact=item.foul_impact,
            penalty_duration_seconds=item.penalty_duration_seconds,
        )
        for item in definitions
    ]
