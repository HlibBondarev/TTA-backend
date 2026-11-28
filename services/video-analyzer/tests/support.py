"""Shared helpers for tests: sample snapshot access and stable internal prompt codes."""

from __future__ import annotations

import json
from pathlib import Path

from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.schemas.request import MatchSnapshot, RuntimeHints
from video_analyzer.sport import (
    AnalysisContext,
    EventDefinition,
    SportProfile,
    build_analysis_context,
)

SAMPLE_SNAPSHOT_PATH = (
    Path(__file__).resolve().parents[1] / "sample_snapshots" / "waterpolo_default_snapshot.json"
)
DEFAULT_TRACKED_CAP_HINT = "white"
DEFAULT_OPPONENT_CAP_HINT = "blue"


def load_sample_snapshot() -> MatchSnapshot:
    """Parse the committed water polo analysis-snapshot fixture."""
    raw = json.loads(SAMPLE_SNAPSHOT_PATH.read_text(encoding="utf-8"))
    return MatchSnapshot.model_validate(raw)


def make_context(
    snapshot: MatchSnapshot | None = None,
    hints: RuntimeHints | None = None,
    profile: SportProfile | None = None,
) -> AnalysisContext:
    """Build an analysis context with the fixture's white/blue cap hints by default."""
    return build_analysis_context(
        snapshot or load_sample_snapshot(),
        hints
        if hints is not None
        else RuntimeHints(
            tracked_cap_color_hint=DEFAULT_TRACKED_CAP_HINT,
            opponent_cap_color_hint=DEFAULT_OPPONENT_CAP_HINT,
        ),
        profile,
    )


def prompt_code_for(name: str, positive: bool = True) -> str:
    """Return the generated internal prompt code of a sample system event definition."""
    for definition in make_context().event_catalog:
        if definition.name == name and definition.is_positive == positive:
            return definition.prompt_code
    raise LookupError(f"No sample event definition named {name!r} (positive={positive}).")


CODE_GOAL = prompt_code_for("Goal", positive=True)
CODE_STEAL = prompt_code_for("Steal")
CODE_KEY_PASS = prompt_code_for("Key Pass")
CODE_GOAL_CONCEDED = prompt_code_for("Goal", positive=False)


def find_definition(
    context: AnalysisContext, name: str, is_positive: bool = True
) -> EventDefinition:
    """Return the catalog definition with the given name and polarity."""
    return next(d for d in context.event_catalog if d.name == name and d.is_positive is is_positive)


def make_candidate(
    context: AnalysisContext,
    name: str = "Goal",
    is_positive: bool = True,
    *,
    time_sec: float = 20.0,
    bbox: list[float] | None = None,
    cap_color_hint: str | None = "white",
    pool_zone_hint: str | None = "center",
    attack_direction: str = "unknown",
) -> Pass1EventCandidate:
    """Build a Pass 1 candidate for ``name`` with sensible Pass 2 defaults."""
    return Pass1EventCandidate(
        video_time_sec=time_sec,
        definition=find_definition(context, name, is_positive),
        period_number=1,
        is_lead_to_goal=False,
        actor_bbox_normalized=bbox if bbox is not None else [0.4, 0.4, 0.6, 0.6],
        pool_zone_hint=pool_zone_hint,
        cap_color_hint=cap_color_hint,
        confidence=0.92,
        tracked_attack_direction=attack_direction,  # type: ignore[arg-type]
    )
